// ==============================================================================================
//  Vector - the column-expression calculation and recursion stack
//  --------------------------------------------------------------------------------------------
//  SUBSTITUTED FOR  ws_objects/pfw.utility.container.pbl.src/n_vector.sru (54 lines)
//  ORACLE STATUS    That .sru is the ONLY specification for this type, and it is READ ONLY: it
//                   is the behavioural oracle for parity testing, never an edit target. Every
//                   member below cites the n_vector.sru line it was taken from.
//
//  THIS IS A SUBSTITUTION, NOT A PORT, AND THAT IS A MEASURED FACT
//  --------------------------------------------------------------------------------------------
//  n_vector.sru:L8 declares
//
//      global type n_vector from nonvisualobject native "pfw.dll"
//
//  and the file contains ZERO function or subroutine bodies. Every line of behaviour lives
//  inside the closed-source pfw.dll, for which no C++ source exists anywhere in this
//  repository. There is therefore no PowerScript to transliterate: the 33 prototypes at
//  L9-L41 ARE the entire specification, and this file re-expresses them on the base class
//  library.
//
//  Where a prototype alone does not settle a behaviour, the choice made below is labelled a
//  DOCUMENTED INTERPRETATION at its point of reproduction. That label means the behaviour is
//  defined, reproducible and covered by a test. It is never a claim that the behaviour was
//  verified against the binary. Should a characterization recording later contradict one of
//  these choices, the recording wins and the member is corrected; until then nothing here is
//  left undefined, because an undefined cursor or index is exactly the kind of hole that
//  produces an irreproducible defect in the recursion stack this type exists to carry.
//
//  DP-7 EVIDENCE STATUS, STATED IN THE TERMS A PARITY REPORT USES
//  --------------------------------------------------------------------------------------------
//  Naming the standard explicitly, because "DOCUMENTED INTERPRETATION" above says what the label
//  means but not what it is NOT: no DOCUMENTED INTERPRETATION in this file is GOLDEN-MASTER
//  PARITY. Golden-Master parity means the port's output was compared against a RECORDING of the
//  legacy's output, and no recording of this type's behaviour exists anywhere in the repository.
//  The tests covering these choices are TARGET REGRESSION GUARDS: they catch a change to this
//  file, which is genuinely valuable, and they cannot detect a disagreement with pfw.dll.
//
//  Promoting any DOCUMENTED INTERPRETATION to parity evidence requires a paired recording under
//  characterization/recordings/{legacy,dotnet}/<workflowId>/, captured against one unrecreated
//  persistence-db volume state. Until then none of them may be reported as verified legacy
//  behaviour in docs/PARITY.md or in any published summary. The member citations below are
//  signature evidence, which the prototypes fully supply, and nothing more.
//
//  THE WHOLE OF THE LEGACY SURFACE, ENUMERATED
//  --------------------------------------------------------------------------------------------
//  Transcribed from n_vector.sru so that the surface below can be diffed against the source
//  top to bottom, in the source's own declaration order. Every member in this file is either
//  one of these prototypes or one of the two annotated exceptions; there is no third category.
//
//      L9   long copyfromlist(n_list obj)                       OMITTED       see C-D below
//      L10  long copyfromvector(n_vector obj)                   CopyFromVector
//      L11  long copytoarray(ref any values[])                  CopyToArray
//      L12  ulong count()                                       Count
//      L13  ulong maxsize()                                     MaxSize
//      L14  subroutine purge()                                  Purge
//      L15  subroutine reverse()                                Reverse
//      L16  boolean reserve(ulong size)                         Reserve
//      L17  boolean resize(ulong size)                          Resize
//      L18  boolean exists(any value)                           Exists
//      L19  subroutine move(ulong index)                        Move
//      L20  subroutine append(any value)                        Append
//      L21  any get()                                           Get
//      L22  any getat(ulong index)                              GetAt
//      L23  subroutine set(any value)                           Set
//      L24  subroutine setat(ulong index, any value)            SetAt
//      L25  subroutine remove()                                 Remove
//      L26  subroutine removeat(ulong index)                    RemoveAt
//      L27  any getnext()                                       GetNext
//      L28  any getprevious()                                   GetPrevious
//      L29  any getandnext()                                    GetAndNext
//      L30  subroutine rewind()                                 Rewind
//      L31  any getfirst()                                      GetFirst
//      L32  any getlast()                                       GetLast
//      L33  subroutine prepend(any value)                       Prepend
//      L34  subroutine insertbefore(ulong index, any value)     InsertBefore
//      L35  subroutine insertafter(ulong index, any value)      InsertAfter
//      L36  boolean hasnext()                                   HasNext
//      L37  ulong position()                                    Position()
//      L38  ulong position(any value)                           Position(value)
//      L39  subroutine sort()                                   Sort()
//      L40  subroutine sort(powerobject, string cmpfunc)        SUBSTITUTED   see C-D below
//      L41  subroutine unique()                                 Unique
//
//  The arithmetic, stated so nobody miscounts it later: 33 prototypes are declared, 1 is
//  omitted, so 32 legacy declarations ship. One of the 32 (L40) maps to TWO C# overloads, which
//  makes 33 public methods in the finished type. Type mapping is the plan's own: any -> object?,
//  ulong -> ulong, long -> long, a ref parameter stays a ref parameter, and powerobject ->
//  object, which is precisely why L40 has to be substituted rather than mapped. count(),
//  maxsize() and position() stay METHODS rather than becoming properties, mirroring the legacy
//  function shapes so the surface diffs cleanly against the source.
//
//  ONE-BASED INDEXING IS PROVEN FROM CONSUMER CODE, NOT ASSUMED FROM CONVENTION
//  --------------------------------------------------------------------------------------------
//  The plan names one-based to zero-based translation the single most dangerous mechanical
//  hazard in this refactor. For this type it is not a convention argument, because the only
//  in-scope consumer settles it mechanically. In
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:
//
//      L296   _vecCalcStack.Append(colName)     push
//      L297   k = _vecCalcStack.Count()         k is taken as the index OF THE JUST-APPENDED
//                                               element, with no adjustment
//      L318   _vecCalcStack.RemoveAt(k)         pop exactly that element
//
//  Under zero-based indexing RemoveAt(Count()) would be one past the end, so the push would be
//  followed by a pop of nothing and the stack would grow without bound. The last element's
//  index therefore EQUALS Count(), which means valid indices run 1 .. Count() inclusive.
//  Corroborated twice more by inclusive iteration that never touches index 0:
//
//      L684-L686   nCount = Count() : for nIndex = 1 to nCount : if GetAt(nIndex) = ... then
//                  the recursion guard, which is why this container is the recursion stack
//      L753-L755   nCount = Count() : for nIndex = 1 to nCount : sCallStack += GetAt(nIndex) + ">"
//
//  That second loop is the one that makes the stakes concrete rather than academic. The string
//  it builds is handed straight to the trace event at the same object's L757-L758,
//  OnColumnExpTrace(row, dwo, sCallStack, sExp, ...), which is carried on the expression-trace
//  channel of cross-service contract C-04. A zero-based or reversed GetAt would therefore
//  corrupt an OBSERVABLE WIRE PAYLOAD, not merely an internal index, and it would do so while
//  every element count still matched. That is exactly the class of silent regression the plan
//  warns about, so the conversion is centralised in ONE place in this file: ToSlot below. No
//  other member performs index arithmetic, and CopyToArray is the only other place where the
//  one-based world meets a zero-based one.
//
//  THE SURFACE IS A C++ STL VECTOR FACADE, WHICH SETTLES THE OTHERWISE-AMBIGUOUS MEMBERS
//  --------------------------------------------------------------------------------------------
//  Reading L9-L41 as a whole, the naming maps one for one onto std::vector and the algorithms
//  that accompany it: count/maxsize/reserve/resize/purge onto size/capacity/reserve/resize/
//  clear, and reverse/sort/unique onto std::reverse, std::sort and std::unique. That
//  correspondence is the evidence behind three interpretations that a signature alone cannot
//  settle - MaxSize reporting allocated capacity, Unique collapsing only CONSECUTIVE
//  duplicates, and the past-the-end cursor state described next - and it is recorded here so a
//  future reader knows where those semantics came from rather than assuming they were guessed.
//
//  THE CURSOR IS PART OF THE TYPE, NOT AN ORNAMENT
//  --------------------------------------------------------------------------------------------
//  Eleven of the shipped members - Move, Get, Set, Remove, GetNext, GetPrevious, GetAndNext,
//  Rewind, GetFirst, GetLast, HasNext and Position() - read or write a single position that
//  persists between calls. This container is therefore a stateful cursor as well as a sequence,
//  and that is precisely what makes it usable as an expression engine's recursion stack. A bare
//  List<object?> substitute would silently drop it and break recursion tracking, so the cursor
//  is held explicitly below and every member that can move it says so.
//
//  The cursor is one-based like the indices, and it spans 0 .. Count() + 1:
//
//      0             before the first element - the rewound, unset state
//      1 .. Count()  positioned on that element
//      Count() + 1   past the last element, the std::vector end() analogue
//
//  The past-the-end state exists for one reason and is reachable only one way. GetAndNext
//  returns the current element and THEN advances, so reading the last element must leave the
//  cursor somewhere that yields null on the next call; otherwise the documented idiom
//  Move(1) followed by repeated GetAndNext would return the final element for ever instead of
//  visiting each element exactly once. Clamping to Count() would produce that infinite repeat,
//  so the cursor is allowed to settle one past the end, exactly as an STL iterator does. It is
//  clamped back to Count() by the next mutation that shortens the container.
//
//  GOVERNING CONSTRAINTS
//  (review_rules returns exactly one line, "No user rules provided", so no user rule governs
//  this file. The binding constraints are the plan's own inventory, honoured here exactly as
//  rules would have been, and nothing below is an invented rule.)
//  --------------------------------------------------------------------------------------------
//  C-A  Shared IMPLEMENTATION consumed in process by DataServices through a ProjectReference,
//       never a cross-service channel. So: no serialization attribute, no DTO or .proto
//       shaping, no gRPC or HTTP awareness, no logging, no configuration and no I/O. Pure
//       behaviour, and pure enough that this project references nothing at all - not even
//       PowerFramework.Shared.Kernel, because the prototypes at L9-L41 name no framework type.
//  C-B  Exactly the 32 shipped declarations and nothing more. Behaviour is replicated, never
//       improved, and the cursor is not collapsed away for the convenience of a plain list.
//       The unrequested additions this rules out are listed under DELIBERATELY ABSENT below.
//  C-C  The legacy tree is read only. n_vector.sru, n_list.sru and n_cst_dwsvc_columnexp.sru
//       were read as the specification and left untouched, and every behaviour cites its :L
//       locator so a reviewer can check it against the oracle without editing it.
//  C-D  The two deferred-service boundaries are respected by an omission and a substitution,
//       each documented at the position of the prototype it replaces: L9 copyfromlist, whose
//       parameter type belongs to the deferred Documents service, and L40 sort by method-name
//       string, whose dispatch mechanism belongs to the deferred ScriptBridge service.
//  C-H  Every member is reachable and assertable from the public surface, and the cursor is
//       observable through Position() so cursor behaviour can be tested rather than inferred.
//       There is deliberately no unreachable defensive branch: ClampCursor is called only by
//       the members that can actually shorten the container, and the boolean returns of
//       Reserve and Resize are genuine, reachable failure paths rather than constant true.
//  C-K  Every technology-specific decision is documented where it is reproduced: the
//       List<object?> substitution and the STL correspondence above, the explicit cursor, the
//       one-based contract and its trace-payload consequence, both C-D boundaries, each
//       documented interpretation, the global auto-instance resolution, the deliberate absence
//       of thread safety, and the IDisposable non-port.
//
//  Constraints that do NOT apply, recorded so that nothing is invented to satisfy them: C-E
//  (no database is touched), C-F (no configuration or secret surface exists here, and no
//  credential-shaped literal appears), C-G (this library opens no boundary to authenticate),
//  C-I and C-J (a library is neither a service build unit nor a deployable).
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * IEnumerable<T>, IList<T>, ICollection<T>, an indexer, Add/Clear/Contains/IndexOf
//      aliases, a Count property beside Count(), LINQ helpers, ToArray/ToList, AsSpan, cloning,
//      an Equals or ToString override, and a generic Vector<T>. None appears at L9-L41. Each
//      would be a new feature under C-B, and the interface ones would additionally invite a
//      foreach over a type whose iteration contract is the cursor, not an enumerator.
//    * Push, Pop and Peek. The stack role is real but it is the CONSUMER's reading of
//      Append/Count/RemoveAt, not a member of the legacy surface. Adding the sugar would put
//      two spellings of one operation into a type whose whole job is byte-faithful parity.
//    * IDisposable and a finalizer. n_vector.sru:L45-L53 raises constructor and destructor
//      events, and the consumer really does pair Create n_vector at
//      n_cst_dwsvc_columnexp.sru:L2421 with Destroy _vecCalcStack at L2425 - but the
//      substituted implementation owns nothing but managed memory, so there is nothing to
//      release deterministically and the ceremony would be pure noise.
//    * Locks, a concurrent collection, or any other synchronisation. This type is NOT thread
//      safe, deliberately. The legacy concurrency model exists so that no object is ever
//      touched from two threads, and the only in-scope consumer holds this instance in a
//      private per-object field [n_cst_dwsvc_columnexp.sru:L110]. Synchronisation would be an
//      unrequested addition under C-B and would misrepresent the threading contract.
//    * A static instance, singleton property or any static mutable state. n_vector.sru:L43
//      declares `global n_vector n_vector`, a global auto-instance shadowing its own type name.
//      The plan's resolution rule applies verbatim: the TYPE keeps the descriptive .NET name
//      and the INSTANCE becomes a constructed or injected dependency instead of a global. The
//      only in-scope consumer corroborates this by instantiating its own with Create n_vector
//      at L2421 and never touching the global at all.
//    * SCREAMING_SNAKE and lowercase legacy identifiers. The repository-root .editorconfig
//      scopes its naming-analyzer suppressions to a closed list of files that genuinely carry
//      preserved legacy constants, and this file is not on it - nor does it declare a constant
//      of any kind. With TreatWarningsAsErrors inherited from Directory.Build.props, a
//      non-conventional identifier here would be a build error, so legacy spellings appear
//      only in documentation, never in an identifier.
// ==============================================================================================

namespace PowerFramework.Shared.Containers;

/// <summary>
/// A sequential container of <see langword="object"/> values that is also a stateful, one-based
/// cursor: the managed substitute for the legacy PowerBuilder type <c>n_vector</c>, declared at
/// <c>ws_objects/pfw.utility.container.pbl.src/n_vector.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a substitution onto base class library types rather than a port. The legacy type is a
/// PBNI binding - <c>n_vector.sru:L8</c> reads <c>native "pfw.dll"</c> - and its <c>.sru</c>
/// carries the 33 prototypes at <c>L9-L41</c> and not one line of implementation, so the
/// prototypes are the whole specification. Storage is a <c>List</c> of nullable
/// <see langword="object"/>; the cursor is a separate explicit field, because eleven members
/// depend on it and a plain list would drop it.
/// </para>
/// <para>
/// Its role in this refactor is the calculation and recursion stack of the column-expression
/// engine, held at <c>n_cst_dwsvc_columnexp.sru:L110</c>, reserved to twenty elements at
/// <c>L2422</c>, pushed and popped around each column calculation at <c>L296-L318</c>, scanned
/// for the recursion guard at <c>L684-L686</c>, and joined into the trace call stack at
/// <c>L753-L755</c>.
/// </para>
/// <para>
/// <b>Indices are ONE-BASED throughout</b>, spanning <c>1 .. Count()</c> inclusive, and the
/// value <c>0</c> is never a valid element index - which is what frees it to be the not-found
/// sentinel returned by <see cref="Position(object?)"/>. An out-of-range index is not an
/// exception: the accessors return <see langword="null"/> and the subroutines do nothing,
/// because the legacy prototypes have no failure channel to report one through.
/// </para>
/// <para>
/// <b>This type is not thread safe</b>, deliberately and by contract rather than by omission;
/// see the file header. Instances are constructed by their owner and held privately, exactly as
/// the legacy consumer does, and the legacy global auto-instance at <c>n_vector.sru:L43</c> has
/// no counterpart here.
/// </para>
/// </remarks>
public sealed class Vector
{
    /// <summary>
    /// The backing store. A <c>List</c> of nullable <see langword="object"/> is the substitution
    /// for the closed native storage: it gives the amortised append, positional access,
    /// contiguous ordering, and separate count-versus-capacity pair that <c>L9-L41</c> assume.
    /// </summary>
    /// <remarks>
    /// Zero-based, and the ONLY zero-based thing in this file. Nothing outside
    /// <see cref="ToSlot(ulong)"/> and <see cref="CopyToArray(ref object?[])"/> is permitted to
    /// bridge between this field's indexing and the one-based public contract.
    /// </remarks>
    private readonly List<object?> _items = [];

    /// <summary>
    /// The cursor: a one-based position spanning <c>0 .. Count() + 1</c>, where <c>0</c> is the
    /// rewound state before the first element and <c>Count() + 1</c> is the past-the-end state
    /// described in the file header.
    /// </summary>
    /// <remarks>
    /// Held as a plain <see langword="int"/> and converted at the public boundary, so that the
    /// arithmetic stays in one place. A newly constructed <see cref="Vector"/> starts rewound,
    /// which is the default value and therefore needs no constructor.
    /// </remarks>
    private int _cursor;

    /// <summary>
    /// The single one-based to zero-based conversion point in this file: validates a one-based
    /// public index and yields the backing slot, or <c>-1</c> when the index does not reference
    /// an existing element.
    /// </summary>
    /// <param name="oneBasedIndex">
    /// A one-based index. Valid values are <c>1 .. Count()</c> inclusive; <c>0</c> and anything
    /// beyond the last element are out of range.
    /// </param>
    /// <returns>
    /// The zero-based slot in <see cref="_items"/>, or <c>-1</c> to signal out of range. The
    /// sentinel is deliberate: every caller must branch on it, which is what makes the
    /// out-of-range behaviour of the public surface uniform and visible rather than incidental.
    /// </returns>
    /// <remarks>
    /// Centralising the conversion here is a direct response to the plan naming one-based
    /// translation the most dangerous mechanical hazard in this refactor. <c>index - 1</c>
    /// appears exactly once in this file, in this method, and every positional member routes
    /// through it - including the cursor dereference, so the cursor cannot drift away from the
    /// index contract it shares. The helper is private because this project's file list is
    /// fixed at the project file plus its two sources, so there is no third file to host it.
    /// </remarks>
    private int ToSlot(ulong oneBasedIndex)
    {
        if (oneBasedIndex == 0 || oneBasedIndex > (ulong)_items.Count)
        {
            return -1;
        }

        return (int)(oneBasedIndex - 1);
    }

    /// <summary>
    /// Pulls the cursor back to the last valid position after the container has been shortened,
    /// so that it can never reference beyond the end.
    /// </summary>
    /// <remarks>
    /// Cursor maintenance, not index conversion, which is why it is separate from
    /// <see cref="ToSlot(ulong)"/>. It is called ONLY by the members that can actually reduce
    /// the element count - <see cref="Resize(ulong)"/>, <see cref="Remove"/>,
    /// <see cref="RemoveAt(ulong)"/> and <see cref="Unique"/> - and deliberately not by
    /// <see cref="Append(object?)"/>, <see cref="Prepend(object?)"/>,
    /// <see cref="InsertBefore(ulong, object?)"/>, <see cref="InsertAfter(ulong, object?)"/>,
    /// <see cref="Sort()"/> or <see cref="Reverse"/>, where the count grows or holds and the
    /// clamp could therefore never fire. Calling it there would add a branch no test could ever
    /// exercise, which C-H forbids.
    /// </remarks>
    private void ClampCursor()
    {
        if (_cursor > _items.Count)
        {
            _cursor = _items.Count;
        }
    }

    // ------------------------------------------------------------------------------------------
    //  n_vector.sru:L9   public function long copyfromlist(n_list obj)
    //
    //  OMITTED. NOT implemented here, NOT implemented under any other name, and NOT substituted
    //  by an overload taking a list. This comment sits in L9's position in the declaration order
    //  so that a reviewer diffing this file against the source finds the explanation exactly
    //  where they look for the missing member.
    //
    //  The reason is constraint C-D. The parameter type n_list is assigned to the DEFERRED
    //  Documents service, and C-D forbids implementing any part of a deferred service, even
    //  partially and even to stub it out. Accepting a list-shaped argument here would be
    //  implementing that boundary in all but name.
    //
    //  The closure argument, which is what makes the omission auditable rather than merely
    //  compliant, was measured rather than assumed. A repository-wide search for n_list as a
    //  whole word across every .sru, .srw and .srf file, excluding n_list.sru itself, returns
    //  EXACTLY ONE hit:
    //
    //      ws_objects/pfw.utility.container.pbl.src/n_vector.sru:9
    //          public function long copyfromlist(n_list obj)
    //
    //  Not one object anywhere in the estate declares a variable of that type. Omitting this
    //  single member therefore leaves the deferred type with ZERO references from in-scope
    //  code: the C-D boundary closes exactly, by one omission, with no residue to explain away
    //  and nothing for a later phase to unpick.
    //
    //  Worth recording alongside it, because it explains why only one of the two sibling
    //  containers needed to be in scope at all: n_vector IS n_list plus reserve and resize.
    //  Comparing the two exports line by line, n_list.sru declares thirty-one members at
    //  L9-L39 and n_vector.sru declares those same thirty-one plus exactly reserve
    //  [n_vector.sru:L16] and resize [n_vector.sru:L17]. Nothing in n_list is unreachable
    //  through this type.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces this container's entire contents with a copy of another vector's elements.
    /// Legacy counterpart <c>n_vector.sru:L10 - long copyfromvector(n_vector obj)</c>.
    /// </summary>
    /// <param name="obj">
    /// The vector to copy from. Its contents and its cursor are both left untouched. Passing this
    /// same instance is explicitly supported and is a no-op on the contents.
    /// </param>
    /// <returns>The number of elements copied, which is this container's new element count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// DOCUMENTED INTERPRETATION - this REPLACES rather than appends. The prototype is named
    /// "copy from" and not "append from", and a copy that left existing elements in place would
    /// make the returned count ambiguous between elements copied and elements held. Replacement
    /// is the reading that keeps the return value meaningful.
    /// </para>
    /// <para>
    /// DOCUMENTED INTERPRETATION - the cursor is reset to the rewound state rather than clamped.
    /// Every element the cursor could have referred to has just been discarded, so preserving a
    /// numeric position would leave it pointing at an unrelated element that merely happens to
    /// occupy the same slot. This is the one mutation where clamping would be misleading.
    /// </para>
    /// <para>
    /// Rejecting <see langword="null"/> is a .NET convention applied to a parameter contract this
    /// substitution introduces, not a legacy behaviour being changed: the legacy parameter is a
    /// PowerBuilder object reference and the prototype offers no channel to report a bad one.
    /// </para>
    /// </remarks>
    public long CopyFromVector(Vector obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        // Snapshot the source BEFORE clearing. That is what makes v.CopyFromVector(v) safe: when
        // obj and this are the same instance, clearing first would discard the very elements
        // about to be copied. Snapshotting removes the need for a self-copy branch altogether
        // rather than guarding against it, so there is no special case to get wrong.
        object?[] snapshot = obj._items.ToArray();

        _items.Clear();
        _items.AddRange(snapshot);
        _cursor = 0;

        return _items.Count;
    }

    /// <summary>
    /// Copies every element into a newly allocated array, in ascending one-based order.
    /// Legacy counterpart <c>n_vector.sru:L11 - long copytoarray(ref any values[])</c>.
    /// </summary>
    /// <param name="values">
    /// Receives a newly allocated array holding the elements. Any array passed in is ignored and
    /// overwritten, never appended to or reused, so its incoming length and contents are
    /// irrelevant. The parameter stays <see langword="ref"/> rather than becoming
    /// <see langword="out"/> or a return value, mirroring the legacy <c>ref</c> parameter.
    /// </param>
    /// <returns>
    /// The number of elements written, which is <see cref="Count"/> at the moment of the call.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SECOND AND ONLY OTHER ONE-BASED BOUNDARY IN THIS FILE. The produced array is a
    /// conventional zero-based .NET array, so logical element 1 lands at <c>values[0]</c>,
    /// element 2 at <c>values[1]</c>, and logical element <see cref="Count"/> at
    /// <c>values[Count() - 1]</c>. Order is preserved exactly, so a caller that walks the array
    /// forwards sees the same sequence as a caller that walks <see cref="GetAt(ulong)"/> from
    /// <c>1</c> to <see cref="Count"/>.
    /// </para>
    /// <para>
    /// An empty container yields a zero-length array and a return value of <c>0</c>; it never
    /// yields <see langword="null"/>, so a caller need not distinguish the two cases.
    /// </para>
    /// </remarks>
    public long CopyToArray(ref object?[] values)
    {
        values = _items.ToArray();

        return _items.Count;
    }

    /// <summary>
    /// The number of elements held. Legacy counterpart
    /// <c>n_vector.sru:L12 - ulong count()</c>.
    /// </summary>
    /// <returns>The element count, which is also the highest valid one-based index.</returns>
    /// <remarks>
    /// A METHOD rather than a property, mirroring the legacy function shape so the surface diffs
    /// cleanly against <c>L9-L41</c>; no <c>Count</c> property is offered beside it. The returned
    /// value doubling as the last valid index is the one-based contract proven in the file
    /// header, and it is exactly what the consumer relies on when it appends and then treats
    /// this result as the index of the element it just pushed
    /// [n_cst_dwsvc_columnexp.sru:L296-L297].
    /// </remarks>
    public ulong Count() => (ulong)_items.Count;

    /// <summary>
    /// The currently allocated capacity, which is at least <see cref="Count"/>. Legacy
    /// counterpart <c>n_vector.sru:L13 - ulong maxsize()</c>.
    /// </summary>
    /// <returns>The number of elements the container can hold before it must grow.</returns>
    /// <remarks>
    /// DOCUMENTED INTERPRETATION - allocated capacity, not a theoretical maximum. Read as
    /// <c>std::vector::max_size</c> this would report a near-constant ceiling, which would make
    /// the member useless in practice and, worse, would make <see cref="Reserve(ulong)"/>
    /// completely unobservable from the public surface. Read as capacity it is the natural
    /// partner of <see cref="Reserve(ulong)"/>: reserving raises it while leaving
    /// <see cref="Count"/> alone. No in-scope consumer calls this member, so the parity risk of
    /// the interpretation is nil.
    /// </remarks>
    public ulong MaxSize() => (ulong)_items.Capacity;

    /// <summary>
    /// Removes every element. Legacy counterpart
    /// <c>n_vector.sru:L14 - subroutine purge()</c>.
    /// </summary>
    /// <remarks>
    /// CURSOR EFFECT - reset to <c>0</c>, the rewound state. There is no element left for a
    /// position to mean anything relative to, so this is the one clamp-free case among the
    /// shrinking mutations.
    /// </remarks>
    public void Purge()
    {
        _items.Clear();
        _cursor = 0;
    }

    /// <summary>
    /// Reverses the order of the elements in place. Legacy counterpart
    /// <c>n_vector.sru:L15 - subroutine reverse()</c>.
    /// </summary>
    /// <remarks>
    /// CURSOR EFFECT - the numeric position is kept, so the cursor addresses the same slot and
    /// therefore a different element. No clamp is needed or performed, because the element count
    /// does not change; see <see cref="ClampCursor"/> for why an unconditional clamp is
    /// deliberately not applied here.
    /// </remarks>
    public void Reverse() => _items.Reverse();

    /// <summary>
    /// Ensures capacity for at least <paramref name="size"/> elements without changing
    /// <see cref="Count"/>. Legacy counterpart
    /// <c>n_vector.sru:L16 - boolean reserve(ulong size)</c>.
    /// </summary>
    /// <param name="size">The capacity to guarantee. Zero is permitted and does nothing.</param>
    /// <returns>
    /// <see langword="true"/> when the capacity is guaranteed; <see langword="false"/> when
    /// <paramref name="size"/> exceeds the largest array this runtime can allocate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// GENUINELY HONOURED, NOT A STUB. The only in-scope consumer really does call
    /// <c>Reserve(20)</c> immediately after construction
    /// [n_cst_dwsvc_columnexp.sru:L2421-L2422], sizing the recursion stack for the expression
    /// depth it expects, so a no-op implementation would silently discard a real pre-allocation.
    /// The effect is observable through <see cref="MaxSize"/>.
    /// </para>
    /// <para>
    /// DOCUMENTED INTERPRETATION of the boolean return. Unlike the subroutines, this prototype
    /// HAS a failure channel, so it is used as one rather than returning a constant: a request
    /// larger than the runtime's maximum array length is reported as <see langword="false"/>
    /// instead of throwing. That keeps the member total and keeps the branch reachable and
    /// testable, which C-H requires.
    /// </para>
    /// </remarks>
    public bool Reserve(ulong size)
    {
        if (size > (ulong)Array.MaxLength)
        {
            return false;
        }

        _items.EnsureCapacity((int)size);

        return true;
    }

    /// <summary>
    /// Changes the ELEMENT COUNT to <paramref name="size"/>, padding with
    /// <see langword="null"/> when growing and truncating from the end when shrinking. Legacy
    /// counterpart <c>n_vector.sru:L17 - boolean resize(ulong size)</c>.
    /// </summary>
    /// <param name="size">The required element count. Zero empties the container.</param>
    /// <returns>
    /// <see langword="true"/> when the container was resized; <see langword="false"/> when
    /// <paramref name="size"/> exceeds the largest array this runtime can allocate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the member that separates <c>n_vector</c> from its sibling <c>n_list</c>, together
    /// with <see cref="Reserve(ulong)"/>, and the contrast between the two is the whole point:
    /// reserving changes CAPACITY and leaves <see cref="Count"/> alone, whereas resizing changes
    /// <see cref="Count"/> itself. Growth pads with <see langword="null"/> because
    /// <see langword="object"/> has no other neutral value, and because the legacy element type
    /// <c>any</c> is likewise nullable - the plan is explicit that null must be handled rather
    /// than collapsed to a default.
    /// </para>
    /// <para>
    /// CURSOR EFFECT - clamped down to the new <see cref="Count"/> when shrinking would otherwise
    /// leave it beyond the end; unchanged when growing.
    /// </para>
    /// </remarks>
    public bool Resize(ulong size)
    {
        if (size > (ulong)Array.MaxLength)
        {
            return false;
        }

        int target = (int)size;

        if (target < _items.Count)
        {
            _items.RemoveRange(target, _items.Count - target);
        }
        else if (target > _items.Count)
        {
            // Grow the buffer once, then pad, so that repeated single-element growth cannot
            // trigger a sequence of reallocations on the way to a known final size.
            _items.EnsureCapacity(target);

            while (_items.Count < target)
            {
                _items.Add(null);
            }
        }

        ClampCursor();

        return true;
    }

    /// <summary>
    /// Reports whether a value is present. Legacy counterpart
    /// <c>n_vector.sru:L18 - boolean exists(any value)</c>.
    /// </summary>
    /// <param name="value">
    /// The value to look for. <see langword="null"/> is a searchable value, not a missing
    /// argument, and matches an element that is itself <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when at least one element matches.</returns>
    /// <remarks>
    /// Delegates to <see cref="Position(object?)"/> so that this type has exactly ONE equality
    /// implementation. Two independently written comparison loops would be two chances for the
    /// membership test and the index lookup to disagree, and the equality semantics here are
    /// subtle enough - see <see cref="Position(object?)"/> - that the duplication would be a real
    /// risk rather than a stylistic one.
    /// </remarks>
    public bool Exists(object? value) => Position(value) != 0;

    /// <summary>
    /// Positions the cursor on the element at a ONE-BASED index. Legacy counterpart
    /// <c>n_vector.sru:L19 - subroutine move(ulong index)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index. <c>0</c> means the rewound state and is equivalent to
    /// <see cref="Rewind"/>; <c>1</c> to <see cref="Count"/> positions the cursor on that
    /// element; anything greater does nothing at all.
    /// </param>
    /// <remarks>
    /// DOCUMENTED INTERPRETATION of an out-of-range index - a NO-OP that leaves the cursor
    /// exactly where it was, chosen over silently clamping to the last element. A subroutine has
    /// no failure channel, so the caller cannot be told either way; between two silent outcomes
    /// the least destructive one is the honest choice, because clamping would move the cursor to
    /// a position the caller never asked for and a subsequent <see cref="Get"/> would then return
    /// a plausible wrong element instead of leaving the previous position intact. Locked by a
    /// test so it cannot drift.
    /// </remarks>
    public void Move(ulong index)
    {
        if (index == 0)
        {
            _cursor = 0;

            return;
        }

        // Validate through the single conversion helper rather than repeating the range test, so
        // that the cursor's notion of a valid position cannot drift from the indexers'. The
        // validated index is then stored AS IS, because the cursor is one-based like the index: no
        // arithmetic happens here, which keeps ToSlot the only place in this file where the two
        // index bases meet.
        if (ToSlot(index) >= 0)
        {
            _cursor = (int)index;
        }
    }

    /// <summary>
    /// Adds a value to the end, so that it becomes the element at index <see cref="Count"/>.
    /// Legacy counterpart <c>n_vector.sru:L20 - subroutine append(any value)</c>.
    /// </summary>
    /// <param name="value">
    /// The value to add. <see langword="null"/> is a storable value, not a missing argument.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE PUSH HALF OF THE ONE-BASED PROOF, and one of the five members the entire in-scope
    /// parity risk rests on. The consumer appends and then takes <see cref="Count"/> as the index
    /// of the element it just added [n_cst_dwsvc_columnexp.sru:L296-L297], later popping exactly
    /// that element with <see cref="RemoveAt(ulong)"/> [L318]. The post-condition this member owes
    /// that idiom is therefore precise: after the call, <c>GetAt(Count())</c> returns
    /// <paramref name="value"/>.
    /// </para>
    /// <para>
    /// CURSOR EFFECT - the numeric position is kept and remains valid, because the element count
    /// only ever grows here.
    /// </para>
    /// </remarks>
    public void Append(object? value) => _items.Add(value);

    /// <summary>
    /// The element at the cursor. Legacy counterpart
    /// <c>n_vector.sru:L21 - any get()</c>.
    /// </summary>
    /// <returns>
    /// The element at the cursor, or <see langword="null"/> when the cursor is not on one -
    /// meaning it is rewound at <c>0</c> or has settled past the end.
    /// </returns>
    /// <remarks>
    /// Because a stored element may itself be <see langword="null"/>, a
    /// <see langword="null"/> result does not by itself distinguish "no element here" from "the
    /// element here is null". <see cref="Position()"/> is the member that resolves the
    /// difference, which is one of the reasons the cursor is deliberately observable.
    /// </remarks>
    public object? Get()
    {
        int slot = ToSlot((ulong)_cursor);

        return slot < 0 ? null : _items[slot];
    }

    /// <summary>
    /// The element at a ONE-BASED index, without moving the cursor. Legacy counterpart
    /// <c>n_vector.sru:L22 - any getat(ulong index)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index. Valid values run from <c>1</c> to <see cref="Count"/> INCLUSIVE, so the
    /// last element is at <see cref="Count"/> and never at <c>Count() - 1</c>.
    /// </param>
    /// <returns>
    /// The element at <paramref name="index"/>, or <see langword="null"/> when the index does not
    /// reference an existing element. <c>GetAt(0)</c> and <c>GetAt(Count() + 1)</c> therefore
    /// return <see langword="null"/> rather than throwing, because the legacy prototype has no
    /// channel through which to report a bad index.
    /// </returns>
    /// <remarks>
    /// <para>
    /// One of the five members carrying the whole in-scope parity risk, and the one whose
    /// off-by-one would be least visible. The consumer reads it in two inclusive loops that start
    /// at <c>1</c> and end at <see cref="Count"/>: the recursion guard at
    /// [n_cst_dwsvc_columnexp.sru:L684-L686], and the trace call stack at [L753-L755] whose
    /// result is published on the expression-trace channel of contract C-04. A zero-based reading
    /// here would drop the first element and read one past the last while the element COUNT
    /// stayed correct, so it would corrupt that payload without failing any count-based
    /// assertion.
    /// </para>
    /// <para>
    /// Leaving the cursor untouched is what allows the consumer to scan the stack inside a
    /// calculation without disturbing an in-progress traversal.
    /// </para>
    /// </remarks>
    public object? GetAt(ulong index)
    {
        int slot = ToSlot(index);

        return slot < 0 ? null : _items[slot];
    }

    /// <summary>
    /// Overwrites the element at the cursor. Legacy counterpart
    /// <c>n_vector.sru:L23 - subroutine set(any value)</c>.
    /// </summary>
    /// <param name="value">The replacement value; <see langword="null"/> is storable.</param>
    /// <remarks>
    /// DOCUMENTED INTERPRETATION - a NO-OP when the cursor is not on an element, rather than an
    /// exception or an append. This is a subroutine with no failure channel, and the two
    /// alternatives are both worse than doing nothing: throwing would invent an error contract
    /// the prototype does not have, and appending would change the element count from a member
    /// whose entire job is to replace.
    /// </remarks>
    public void Set(object? value)
    {
        int slot = ToSlot((ulong)_cursor);

        if (slot >= 0)
        {
            _items[slot] = value;
        }
    }

    /// <summary>
    /// Overwrites the element at a ONE-BASED index, without moving the cursor. Legacy counterpart
    /// <c>n_vector.sru:L24 - subroutine setat(ulong index, any value)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index, valid from <c>1</c> to <see cref="Count"/> inclusive. An index outside
    /// that range does nothing.
    /// </param>
    /// <param name="value">The replacement value; <see langword="null"/> is storable.</param>
    /// <remarks>
    /// The positional counterpart of <see cref="Set(object?)"/>, and a no-op out of range for the
    /// same reason. The element count is never changed by this member, so it can never be used to
    /// extend the container - <see cref="Append(object?)"/> and <see cref="Resize(ulong)"/> are
    /// the members that do that.
    /// </remarks>
    public void SetAt(ulong index, object? value)
    {
        int slot = ToSlot(index);

        if (slot >= 0)
        {
            _items[slot] = value;
        }
    }

    /// <summary>
    /// Removes the element at the cursor. Legacy counterpart
    /// <c>n_vector.sru:L25 - subroutine remove()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A no-op when the cursor is not on an element, consistent with every other cursor
    /// subroutine here.
    /// </para>
    /// <para>
    /// CURSOR EFFECT, and this one is deliberate rather than incidental - the cursor KEEPS its
    /// numeric position, so after the removal it refers to what was the FOLLOWING element. That
    /// is what makes this member usable inside a forward scan: remove the current element and the
    /// cursor is already sitting on the next one, with no compensating move required. Removing
    /// the last element is the exception that needs the clamp, since the kept position would
    /// otherwise point one past the new end.
    /// </para>
    /// </remarks>
    public void Remove()
    {
        int slot = ToSlot((ulong)_cursor);

        if (slot < 0)
        {
            return;
        }

        _items.RemoveAt(slot);
        ClampCursor();
    }

    /// <summary>
    /// Removes the element at a ONE-BASED index. Legacy counterpart
    /// <c>n_vector.sru:L26 - subroutine removeat(ulong index)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index, valid from <c>1</c> to <see cref="Count"/> INCLUSIVE. An index outside
    /// that range does nothing, so a mistaken <c>0</c> silently removes nothing rather than
    /// removing the first element.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE POP HALF OF THE ONE-BASED PROOF, and the single most load-bearing member in this file.
    /// The consumer pops the element it pushed with the literal call <c>RemoveAt(k)</c>, where
    /// <c>k</c> was taken straight from <see cref="Count"/> after the matching
    /// <see cref="Append(object?)"/> [n_cst_dwsvc_columnexp.sru:L296-L297, L318]. Accepting
    /// <c>Count()</c> as a valid index is therefore not a leniency, it is the contract: a
    /// zero-based implementation would treat that exact call as out of range, remove nothing, and
    /// leave the recursion stack growing on every calculation until the recursion guard at
    /// [L684-L686] started rejecting legitimate columns.
    /// </para>
    /// <para>
    /// CURSOR EFFECT - the numeric position is kept and clamped down to the new
    /// <see cref="Count"/> if the removal left it beyond the end.
    /// </para>
    /// </remarks>
    public void RemoveAt(ulong index)
    {
        int slot = ToSlot(index);

        if (slot < 0)
        {
            return;
        }

        _items.RemoveAt(slot);
        ClampCursor();
    }

    /// <summary>
    /// Advances the cursor and returns the element it lands on. Legacy counterpart
    /// <c>n_vector.sru:L27 - any getnext()</c>.
    /// </summary>
    /// <returns>
    /// The element at the new cursor position, or <see langword="null"/> when there was no next
    /// element - in which case the cursor settles at <see cref="Count"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DOCUMENTED INTERPRETATION - this ADVANCES FIRST and then returns, which is what makes the
    /// natural traversal idiom correct:
    /// </para>
    /// <code>
    /// v.Rewind();
    /// while (v.HasNext())
    /// {
    ///     object? item = v.GetNext();
    /// }
    /// </code>
    /// <para>
    /// Because <see cref="Rewind"/> leaves the cursor before the first element, advancing first is
    /// what makes that loop visit element 1 on its opening iteration and every element exactly
    /// once thereafter. The alternative pairing - return then advance - would skip element 1 after
    /// a rewind, and it is precisely the behaviour <see cref="GetAndNext"/> provides instead, for
    /// a cursor the caller has positioned deliberately. The two members are genuinely distinct and
    /// neither is an alias for the other.
    /// </para>
    /// </remarks>
    public object? GetNext()
    {
        if (!HasNext())
        {
            // Settle at the last element rather than running past it: this is the exhausted state
            // for forward traversal, and HasNext reports false from here.
            _cursor = _items.Count;

            return null;
        }

        _cursor++;

        return Get();
    }

    /// <summary>
    /// Retreats the cursor and returns the element it lands on. Legacy counterpart
    /// <c>n_vector.sru:L28 - any getprevious()</c>.
    /// </summary>
    /// <returns>
    /// The element at the new cursor position, or <see langword="null"/> when there was no
    /// previous element - in which case the cursor settles at <c>0</c>, the rewound state.
    /// </returns>
    /// <remarks>
    /// DOCUMENTED INTERPRETATION - RETREATS FIRST and then returns, the exact mirror of
    /// <see cref="GetNext"/>, so that a backward walk from <see cref="GetLast"/> visits every
    /// element exactly once and then reports exhaustion by returning <see langword="null"/>. From
    /// the past-the-end state left by <see cref="GetAndNext"/> the first retreat lands on the last
    /// element, which keeps the two directions consistent with one another.
    /// </remarks>
    public object? GetPrevious()
    {
        if (_cursor > 1)
        {
            _cursor--;

            return Get();
        }

        _cursor = 0;

        return null;
    }

    /// <summary>
    /// Returns the element at the current cursor position and THEN advances the cursor. Legacy
    /// counterpart <c>n_vector.sru:L29 - any getandnext()</c>.
    /// </summary>
    /// <returns>
    /// The element the cursor was on, or <see langword="null"/> when the cursor was not on an
    /// element - in which case the cursor is not moved.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DOCUMENTED INTERPRETATION - the name reads "get, and then next", so the read happens before
    /// the move. That makes this member the counterpart of <see cref="GetNext"/> for a cursor the
    /// caller has positioned explicitly:
    /// </para>
    /// <code>
    /// v.Move(1);
    /// object? item;
    /// while ((item = v.GetAndNext()) is not null)
    /// {
    /// }
    /// </code>
    /// <para>
    /// Advancing off the last element leaves the cursor one past the end - the
    /// <c>std::vector::end()</c> analogue described in the file header - from where the next call
    /// finds no element and returns <see langword="null"/> without moving again. That is what lets
    /// this idiom terminate having visited every element exactly once. Clamping the cursor to
    /// <see cref="Count"/> instead would make the final element repeat for ever, which is the
    /// concrete reason the past-the-end state exists at all rather than being tidied away.
    /// </para>
    /// </remarks>
    public object? GetAndNext()
    {
        int slot = ToSlot((ulong)_cursor);

        if (slot < 0)
        {
            return null;
        }

        object? value = _items[slot];
        _cursor++;

        return value;
    }

    /// <summary>
    /// Returns the cursor to the position before the first element. Legacy counterpart
    /// <c>n_vector.sru:L30 - subroutine rewind()</c>.
    /// </summary>
    /// <remarks>
    /// CURSOR EFFECT - set to <c>0</c>. This is the state a newly constructed
    /// <see cref="Vector"/> starts in, and the state <see cref="GetNext"/> expects before a full
    /// forward traversal. It leaves the elements completely untouched;
    /// <see cref="Purge"/> is the member that empties the container.
    /// </remarks>
    public void Rewind() => _cursor = 0;

    /// <summary>
    /// Positions the cursor on the first element and returns it. Legacy counterpart
    /// <c>n_vector.sru:L31 - any getfirst()</c>.
    /// </summary>
    /// <returns>
    /// Element <c>1</c>, or <see langword="null"/> when the container is empty - in which case the
    /// cursor is left at <c>0</c>.
    /// </returns>
    /// <remarks>
    /// The cursor lands ON element 1 rather than before it, so a following
    /// <see cref="GetAndNext"/> yields element 1 again while a following <see cref="GetNext"/>
    /// yields element 2. That asymmetry is a direct consequence of the two members' documented
    /// pairings and is intentional.
    /// </remarks>
    public object? GetFirst()
    {
        _cursor = _items.Count == 0 ? 0 : 1;

        return Get();
    }

    /// <summary>
    /// Positions the cursor on the last element and returns it. Legacy counterpart
    /// <c>n_vector.sru:L32 - any getlast()</c>.
    /// </summary>
    /// <returns>
    /// The element at index <see cref="Count"/>, or <see langword="null"/> when the container is
    /// empty - in which case the cursor is left at <c>0</c>.
    /// </returns>
    /// <remarks>
    /// The empty case needs no special handling beyond reading the count, because an empty
    /// container has <see cref="Count"/> of <c>0</c> and <c>0</c> is exactly the rewound cursor
    /// position - one of the small conveniences of the one-based contract. This is also the
    /// natural starting point for a backward walk with <see cref="GetPrevious"/>.
    /// </remarks>
    public object? GetLast()
    {
        _cursor = _items.Count;

        return Get();
    }

    /// <summary>
    /// Inserts a value at the front, so that it becomes element <c>1</c> and every existing
    /// element shifts up by one. Legacy counterpart
    /// <c>n_vector.sru:L33 - subroutine prepend(any value)</c>.
    /// </summary>
    /// <param name="value">
    /// The value to insert. <see langword="null"/> is a storable value, not a missing argument.
    /// </param>
    /// <remarks>
    /// CURSOR EFFECT - the numeric position is KEPT, which for this mutation means the cursor
    /// addresses the same slot and therefore the element that was previously one place earlier. No
    /// clamp is needed because the count only grows. Preserving the number rather than the element
    /// is the consistent rule across every insertion in this type, stated on each so that no
    /// mutation leaves the cursor undefined.
    /// </remarks>
    public void Prepend(object? value) => _items.Insert(0, value);

    /// <summary>
    /// Inserts a value immediately BEFORE the element at a ONE-BASED index, so that the new value
    /// takes that index. Legacy counterpart
    /// <c>n_vector.sru:L34 - subroutine insertbefore(ulong index, any value)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index that must reference an EXISTING element, so <c>1</c> to
    /// <see cref="Count"/> inclusive. Any other value, including <c>0</c> and any index on an
    /// empty container, does nothing.
    /// </param>
    /// <param name="value">The value to insert; <see langword="null"/> is storable.</param>
    /// <remarks>
    /// DOCUMENTED INTERPRETATION - the index must reference an existing element, and out of range
    /// is a no-op. Requiring an existing element is what makes "before" unambiguous, and it keeps
    /// this member consistent with <see cref="Move(ulong)"/>, <see cref="SetAt(ulong, object?)"/>
    /// and <see cref="RemoveAt(ulong)"/>, which all treat exactly <c>1 .. Count()</c> as valid.
    /// Inserting at the front of an empty or non-empty container is <see cref="Prepend(object?)"/>
    /// and appending past the end is <see cref="Append(object?)"/>, so nothing is unreachable
    /// through the surface as a whole.
    /// </remarks>
    public void InsertBefore(ulong index, object? value)
    {
        int slot = ToSlot(index);

        if (slot < 0)
        {
            return;
        }

        _items.Insert(slot, value);
    }

    /// <summary>
    /// Inserts a value immediately AFTER the element at a ONE-BASED index. Legacy counterpart
    /// <c>n_vector.sru:L35 - subroutine insertafter(ulong index, any value)</c>.
    /// </summary>
    /// <param name="index">
    /// A ONE-BASED index that must reference an EXISTING element, so <c>1</c> to
    /// <see cref="Count"/> inclusive. Any other value does nothing.
    /// </param>
    /// <param name="value">The value to insert; <see langword="null"/> is storable.</param>
    /// <remarks>
    /// Passing <see cref="Count"/> appends, which is the one case where this member and
    /// <see cref="Append(object?)"/> coincide - and they coincide only for a non-empty container,
    /// since on an empty one <see cref="Count"/> is <c>0</c> and therefore out of range here.
    /// CURSOR EFFECT - the numeric position is kept, and no clamp is needed because the count
    /// grows.
    /// </remarks>
    public void InsertAfter(ulong index, object? value)
    {
        int slot = ToSlot(index);

        if (slot < 0)
        {
            return;
        }

        _items.Insert(slot + 1, value);
    }

    /// <summary>
    /// Reports whether a forward traversal has an element left to visit. Legacy counterpart
    /// <c>n_vector.sru:L36 - boolean hasnext()</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the container is non-empty AND the cursor has not yet reached
    /// the last element, so that a following <see cref="GetNext"/> will return an element rather
    /// than <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This is the loop guard half of the <see cref="GetNext"/> idiom, and its contract is stated
    /// in terms of that member: it is <see langword="true"/> exactly when
    /// <see cref="GetNext"/> would return an element. It is always <see langword="false"/> on an
    /// empty container, whatever the cursor, and always <see langword="false"/> once the cursor has
    /// reached or passed the last element.
    /// </remarks>
    public bool HasNext() => _items.Count > 0 && _cursor < _items.Count;

    /// <summary>
    /// The current cursor position, ONE-BASED. Legacy counterpart
    /// <c>n_vector.sru:L37 - ulong position()</c>.
    /// </summary>
    /// <returns>
    /// <c>0</c> when the cursor is rewound, <c>1</c> to <see cref="Count"/> when it is on an
    /// element, or <c>Count() + 1</c> when a traversal with <see cref="GetAndNext"/> has run past
    /// the end.
    /// </returns>
    /// <remarks>
    /// Deliberately observable, and not merely as a convenience: it is the only way a caller - or a
    /// test - can tell "the cursor is not on an element" from "the element under the cursor is
    /// <see langword="null"/>", since <see cref="Get"/> returns <see langword="null"/> for both. It
    /// is what makes every documented cursor effect in this type assertable rather than a matter of
    /// trust.
    /// </remarks>
    public ulong Position() => (ulong)_cursor;

    /// <summary>
    /// The ONE-BASED index of the first element equal to a value, or <c>0</c> when there is none.
    /// Legacy counterpart <c>n_vector.sru:L38 - ulong position(any value)</c>.
    /// </summary>
    /// <param name="value">
    /// The value to look for. <see langword="null"/> is a searchable value and matches an element
    /// that is itself <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The one-based index of the first match, or <c>0</c> when the value is absent.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>0</c> IS THE NOT-FOUND SENTINEL, and it is unambiguous precisely because indices are
    /// one-based: <c>0</c> can never be a valid element index, so no match is confusable with a
    /// match at the front. This mirrors PowerBuilder's own <c>Pos()</c> convention, which returns
    /// zero for absent. A caller that mistook <c>0</c> for a valid index and passed it to
    /// <see cref="RemoveAt(ulong)"/> would corrupt a recursion stack, so the sentinel is covered by
    /// a test rather than left to the reader.
    /// </para>
    /// <para>
    /// EQUALITY SEMANTICS, and this is a substantive choice rather than a detail. Comparison uses
    /// the default equality comparer for <see langword="object"/>, which gives VALUE equality for
    /// boxed scalars and strings while falling back to REFERENCE equality for types that do not
    /// override equality. That mixture is exactly what the legacy usage needs, since the sibling
    /// container in this project is used with string keys in one place and with live object values
    /// in another. Reference equality alone would be silently wrong: a lookup for a boxed number or
    /// an equal string would fail to find an element that is present, and no count-based assertion
    /// would ever reveal it.
    /// </para>
    /// </remarks>
    public ulong Position(object? value)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (EqualityComparer<object?>.Default.Equals(_items[i], value))
            {
                // Convert the zero-based loop variable to the one-based public contract exactly
                // once, on the way out. Index 1 is the first element, so 0 stays free below as the
                // not-found sentinel.
                return (ulong)(i + 1);
            }
        }

        return 0;
    }

    /// <summary>
    /// Sorts the elements in place into their natural order. Legacy counterpart
    /// <c>n_vector.sru:L39 - subroutine sort()</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two elements cannot be compared with one another - for example a boxed number and a string.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ordering is the default comparer's, which understands any element implementing comparison and
    /// places <see langword="null"/> before every non-null value. Mutually incomparable boxed types
    /// make that comparer throw, and that exception is deliberately allowed to propagate rather than
    /// being swallowed or pre-empted by a type check: it is the base library's own behaviour for a
    /// heterogeneous collection, and papering over it would substitute an invented ordering for a
    /// clear failure. No in-scope consumer calls this member.
    /// </para>
    /// <para>
    /// CURSOR EFFECT - the numeric position is kept, so the cursor addresses the same slot and
    /// therefore, in general, a different element. The element count cannot change, so no clamp is
    /// applied.
    /// </para>
    /// </remarks>
    public void Sort() => _items.Sort();

    // ------------------------------------------------------------------------------------------
    //  n_vector.sru:L40   public subroutine sort(powerobject apo_comparator, string cmpfunc)
    //
    //  SUBSTITUTED, NOT REPRODUCED. The two overloads below stand in this prototype's position in
    //  the declaration order, so that a reviewer diffing against the source finds the replacement
    //  exactly where the original sits.
    //
    //  What the legacy overload does is dispatch to a comparison routine BY METHOD-NAME STRING on
    //  an arbitrary powerobject: the caller hands over an object and the name of one of its
    //  functions, and the sort resolves that name at run time on every comparison. Reproducing it
    //  faithfully would mean implementing dynamic invocation by name - and the plan assigns the
    //  legacy dynamic-invocation natives to the DEFERRED ScriptBridge service. Building a
    //  name-dispatch mechanism here would therefore be implementing part of a deferred service,
    //  which constraint C-D forbids outright, "even partially, even to stub them out".
    //
    //  The substitution is the .NET comparison contract instead: an IComparer for the primary form
    //  and a Comparison delegate for the lambda-friendly form. Both are named together in the
    //  plan's own brief for this file, so neither is invented. The delegate form is implemented in
    //  terms of the base library's own overload rather than by wrapping itself in an adapter, so
    //  there is exactly one sorting path.
    //
    //  This trades a run-time string lookup for compile-time type safety, which IS a safety
    //  improvement - and it is permitted precisely because it is UNOBSERVABLE in behaviour. The
    //  plan allows the implementation to be safer than the legacy where the change cannot be
    //  observed: the ordering produced for a given comparison is identical, and the only thing that
    //  differs is that a misspelled routine name can no longer get as far as run time. Nothing
    //  about the sorted result changes, so no behavioural parity is spent on it.
    //
    //  Null arguments are rejected with the ordinary .NET exception. That is a NEW parameter
    //  contract introduced by this substitution rather than a legacy behaviour being altered, so
    //  normal .NET conventions govern it; the legacy prototype could not have had an opinion about
    //  a C# comparer reference.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Sorts the elements in place using an explicit comparer. Substituted for
    /// <c>n_vector.sru:L40 - subroutine sort(powerobject apo_comparator, string cmpfunc)</c>; see
    /// the block comment above for why the legacy method-name dispatch is not reproduced.
    /// </summary>
    /// <param name="comparer">
    /// The comparer that defines the ordering. This replaces the legacy pairing of an object and
    /// the name of one of its functions.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="comparer"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The primary substituted form, and the one the delegate overload routes through conceptually.
    /// CURSOR EFFECT - as <see cref="Sort()"/>: the numeric position is kept and no clamp is
    /// applied, because the element count cannot change.
    /// </remarks>
    public void Sort(IComparer<object?> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        _items.Sort(comparer);
    }

    /// <summary>
    /// Sorts the elements in place using a comparison delegate. Substituted for
    /// <c>n_vector.sru:L40 - subroutine sort(powerobject apo_comparator, string cmpfunc)</c>
    /// alongside the comparer overload.
    /// </summary>
    /// <param name="comparison">
    /// The comparison that defines the ordering. Supplying a lambda here is the closest ergonomic
    /// equivalent of the legacy call, which named a routine on an object.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="comparison"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The delegate form of the substitution. It exists because the legacy caller supplied
    /// BEHAVIOUR rather than a comparer object, and a delegate is the closer analogue of that;
    /// having both forms costs nothing because each defers to the base library's matching overload.
    /// CURSOR EFFECT - as <see cref="Sort()"/>.
    /// </remarks>
    public void Sort(Comparison<object?> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        _items.Sort(comparison);
    }

    /// <summary>
    /// Collapses runs of CONSECUTIVE equal elements down to a single element each. Legacy
    /// counterpart <c>n_vector.sru:L41 - subroutine unique()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DOCUMENTED INTERPRETATION - consecutive duplicates only, NOT a global distinct. The whole of
    /// this type's surface reads as a facade over the C++ standard vector and its algorithms, as the
    /// file header sets out, and in that vocabulary this member is <c>std::unique</c>, which removes
    /// only ADJACENT duplicates. The practical consequence for a caller is that
    /// <see cref="Sort()"/> normally precedes it, since sorting is what brings equal elements
    /// together; calling it on unsorted data removes runs and leaves scattered repeats in place,
    /// which is the correct behaviour and not a defect. The global-distinct reading was rejected on
    /// that evidence, and no in-scope consumer calls this member, so the parity risk of the choice
    /// is nil.
    /// </para>
    /// <para>
    /// Equality is the same default comparer used by <see cref="Position(object?)"/>, so a run of
    /// equal boxed scalars or equal strings collapses even when the elements are distinct objects.
    /// </para>
    /// <para>
    /// CURSOR EFFECT - the numeric position is kept and clamped down to the new <see cref="Count"/>,
    /// because this member can shorten the container.
    /// </para>
    /// </remarks>
    public void Unique()
    {
        // Walk BACKWARDS and compare each element with its predecessor. Removing while iterating in
        // reverse means no index below the removal point shifts, so a run of any length collapses
        // correctly in one pass without a second cursor or a copy. Stopping at 1 rather than 0 is
        // deliberate: element 0 has no predecessor to be a duplicate of, and it is always kept.
        for (int i = _items.Count - 1; i > 0; i--)
        {
            if (EqualityComparer<object?>.Default.Equals(_items[i], _items[i - 1]))
            {
                _items.RemoveAt(i);
            }
        }

        ClampCursor();
    }
}
