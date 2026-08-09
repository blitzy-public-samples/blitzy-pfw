// ==============================================================================================
//  OrderedMap - the insertion-ordered, string-keyed map with ONE-BASED positional access
//  --------------------------------------------------------------------------------------------
//  SUBSTITUTED FOR  ws_objects/pfw.utility.container.pbl.src/n_map.sru (31 lines)
//  ORACLE STATUS    That .sru is the ONLY specification for this type, and it is READ ONLY: it is
//                   an input to parity testing, never an edit target. Every member below cites the
//                   n_map.sru line it is derived from.
//
//                   BUT IT IS A PROTOTYPE LIST, NOT A BEHAVIOURAL ORACLE. It settles names,
//                   arities, parameter types and return types, and it settles NOTHING about edge
//                   behaviour, because it contains no bodies (measured below). The behavioural
//                   oracle proper is the compiled pfw.dll exercised through a characterization
//                   recording, and NO SUCH RECORDING EXISTS YET.
//
//  DP-7 EVIDENCE STATUS - what a citation on a member below does and does not prove
//  --------------------------------------------------------------------------------------------
//  A member's `n_map.sru:Lnn` citation proves that member's SIGNATURE. It does not prove that
//  member's edge behaviour, and this file must not be read as though it did:
//
//    TIER 1  TRACEABLE - the ten member names and their signatures, including Count() returning
//            ulong and remaining a method rather than becoming a property.
//    TIER 3  TARGET-CHARACTERIZED - every edge behaviour, because the prototype cannot express
//            one: the null-from-Get and empty-string-from-GetKey miss sentinels, Add's boolean
//            duplicate refusal, Set's in-place overwrite, the case-sensitivity reading, and the
//            inclusive one-based upper bound. Each is DEFINED, REPRODUCIBLE AND COVERED BY A TEST.
//            None is verified against the binary, and none may be presented as Golden-Master
//            parity in docs/PARITY.md or anywhere else until a paired recording exists under
//            characterization/recordings/{legacy,dotnet}/<workflowId>/.
//
//  SHOULD A CHARACTERIZATION RECORDING LATER CONTRADICT ANY TIER 3 CHOICE, THE RECORDING WINS and
//  the member is corrected. Nothing here is left undefined, because an undefined index or miss
//  sentinel produces an irreproducible defect - but being defined is not the same as being proven.
//
//  THIS IS A SUBSTITUTION, NOT A PORT, AND THE DISTINCTION IS LOAD BEARING
//  --------------------------------------------------------------------------------------------
//  n_map.sru:L8 declares `global type n_map from nonvisualobject native "pfw.dll"`, and the file
//  carries ZERO function and ZERO subroutine bodies. That is measured rather than assumed:
//  `grep -c 'end function\|end subroutine'` over n_map.sru returns 0. Every line of behaviour
//  lives inside the closed-source pfw.dll, for which no C++ source exists anywhere in the
//  repository. There is consequently no PowerScript to transliterate, and the ten prototypes at
//  n_map.sru:L9-L18 ARE the entire specification.
//
//  Semantics below therefore come from exactly three sources and no others:
//      (a) the ten signatures at n_map.sru:L9-L18;
//      (b) PowerBuilder collection convention;
//      (c) behaviour corroborated by a measured legacy call site, cited wherever one exists.
//  Where a signature is genuinely ambiguous, the reading adopted is labelled INTERPRETATION at
//  its point of reproduction. An INTERPRETATION is never presented as verified legacy behaviour,
//  because for this type it cannot be: the oracle is a closed binary and the ambiguity is real.
//
//  THE WHOLE OF THE LEGACY TYPE, ENUMERATED
//  --------------------------------------------------------------------------------------------
//  The legacy object is small, and knowing exactly how small matters: it means every member in
//  this file is either a re-expression of one of the lines below or an explicitly annotated
//  convention addition. There is no third category.
//
//      n_map.sru:L4-L6, L8   global type n_map from nonvisualobject native "pfw.dll"
//      n_map.sru:L9          public function  boolean add(string key, any value)
//      n_map.sru:L10         public function  any get(string key)
//      n_map.sru:L11         public function  any get(int index)
//      n_map.sru:L12         public function  string getkey(int index)
//      n_map.sru:L13         public function  boolean getkeys(ref string keys[])
//      n_map.sru:L14         public function  boolean set(string key, any value)
//      n_map.sru:L15         public function  boolean exists(string key)
//      n_map.sru:L16         public function  boolean remove(string key)
//      n_map.sru:L17         public function  ulong count()
//      n_map.sru:L18         public subroutine  purge()
//      n_map.sru:L20         global n_map n_map                      see DECISION 5
//      n_map.sru:L22-L30     on create / on destroy - TriggerEvent only, see DECISION 6
//
//  Ten public members, no more and no fewer. The legacy type declares no constant, no structure
//  and no instance variable, which is why this file declares no constant either.
//
//  WHERE THE MAP IS USED, AND WHY IT IS IN SCOPE AT ALL
//  --------------------------------------------------------------------------------------------
//  This container is one of only two objects rescued from the otherwise-deferred
//  pfw.utility.container library, because it is the single container consumed by BOTH in-scope
//  services. The complete measured call-site inventory is:
//
//      DataServices   n_cst_dwsvc.sru:L583/L585                  declare and Create
//                     n_cst_dwsvc.sru:L634, L642-L644, L647-L648, L655   seven Set calls
//                     n_cst_dwsvc_contextmenu.sru:L948, L987     declare and receive
//                     n_cst_dwsvc_contextmenu.sru:L1006-L1007    Exists then Get
//      Persistence    n_cst_thread_task_sqlquery.sru:L510/L646   declare and Create
//                     n_cst_thread_task_sqlquery.sru:L652, L658  Exists then Add
//                     n_cst_thread_task_sqlbase.sru:L527/L535    declare and Create
//                     n_cst_thread_task_sqlbase.sru:L539-L540    Exists then Get
//                     n_cst_thread_task_sqlbase.sru:L565         Add
//
//  Two facts fall out of that inventory and shape the whole file. First, only Add, Get(string),
//  Set and Exists are exercised anywhere in the repository - see DECISION 2 for the consequence.
//  Second, the values stored are heterogeneous: a boxed boolean at
//  n_cst_thread_task_sqlquery.sru:L658, and a structure carrying a LIVE datastore-derived object
//  at n_cst_thread_task_sqlbase.sru:L565, read back at L540. That is why the value type is
//  `object?` and why this container must never impose value semantics; see DECISION 8.
//
//  DECISION 1 - THE BACKING STORE IS OrderedDictionary, AND A BARE Dictionary WOULD NOT DO
//  --------------------------------------------------------------------------------------------
//  System.Collections.Generic.OrderedDictionary<TKey, TValue> is the substitute because it is the
//  one BCL type providing insertion order AND positional access together, which is exactly the
//  pair this legacy surface needs: `getkey(int)` and `get(int)` are meaningless without a stable
//  sequence, and `getkeys` must hand back that same sequence.
//
//  A bare Dictionary<string, object?> is NOT sufficient and must not be substituted here. Its
//  enumeration order is explicitly unspecified and is perturbed by removal and regrowth, so the
//  three positional members would return whatever the hash layout happened to yield. That
//  failure mode is silent: it produces plausible values in the wrong order rather than an error.
//
//  It ships in the net10.0 framework reference (assembly System.Collections), which is why the
//  project file carries zero PackageReference; no third-party collection package is needed and
//  none may be added. System.Collections.Generic is in the SDK's default implicit-using set and
//  ImplicitUsings is enabled by the repository-root Directory.Build.props, so this file needs no
//  using directive and deliberately has none. The documented fallback - a key-to-slot dictionary
//  paired with an ordered list - was NOT taken, because the type is present and verified.
//
//  DECISION 2 - ONE-BASED POSITIONAL ACCESS, AND THE HONEST STATE OF ITS EVIDENCE
//  --------------------------------------------------------------------------------------------
//  Get(int) and GetKey(int) are ONE-BASED: entry 1 is the first, entry Count() is the last, and
//  the valid range is 1 .. Count() inclusive. One-based-to-zero-based translation is the single
//  most dangerous mechanical hazard in this migration, because an off-by-one here is
//  indistinguishable from a behavioural regression - it returns a real neighbouring value rather
//  than failing. Two countermeasures are applied. The contract is stated on the API surface, in
//  the doc comment of every member it touches, so no ported call site can adopt it by accident.
//  And the conversion happens in exactly ONE place, ToZeroBasedSlot below, which is the only
//  expression in this file that subtracts one from an index.
//
//  The evidence for one-basedness is stated precisely rather than overstated. n_map's positional
//  surface is UNEXERCISED across the whole repository: searching every legacy source extension
//  for `.getkeys(` and `.getkey(` returns zero matches, and no call site passes an integer to
//  `get`. So the choice rests on PowerBuilder collection convention, where the language's own
//  arrays and DataWindow row and column ordinals are one-based throughout; on the migration
//  plan's explicit instruction to keep these overloads one-based; and on consistency with the
//  sibling Vector in this same library, whose one-based indexing IS mechanically provable from
//  its consumers. No claim of call-site verification is made for THIS type, because there is
//  none to make.
//
//  DECISION 3 - Add AND Set ARE DISTINCT, AND Set-INSERTS IS CORROBORATED
//  --------------------------------------------------------------------------------------------
//  Two separate prototypes, n_map.sru:L9 and L14, both taking (string, any) and both returning
//  boolean, only make sense as different operations, so they are kept distinct: Add refuses a
//  key that is already present, Set accepts it. Neither delegates to the other in a way that
//  would lose its own failure report.
//
//  That Set INSERTS when the key is absent is corroborated by call-site behaviour rather than
//  merely assumed. n_cst_dwsvc.sru:L585 creates an EMPTY map; L634 to L655 populate it using
//  Set exclusively and never Add; L663 returns it; n_cst_dwsvc_contextmenu.sru:L987 receives it
//  and L1006-L1007 read entries back out through Exists and Get. Were Set not to insert,
//  _of_GetColumnValueMap would always return an empty map and the paste-translation feature
//  built on it would be dead code. The RETURN VALUE of Set is a different matter: it is never
//  inspected at any of those seven sites, so Set returning true remains an INTERPRETATION.
//
//  DECISION 4 - NO FAILURE CHANNEL EXISTS, SO EVERY MISS RETURNS A DEFAULT AND NOTHING THROWS
//  --------------------------------------------------------------------------------------------
//  This is the single most consequential reading in the file, and it is forced by the signatures
//  rather than chosen. Where the legacy surface reports failure at all it does so through a
//  boolean return - Add, Set, Exists, Remove and GetKeys all return boolean. The two accessors
//  do not: `any get(...)` at n_map.sru:L10-L11 and `string getkey(int)` at L12 return a bare
//  value with no boolean out-parameter, no reference out-parameter and no return code. There is
//  nowhere for a failure to go.
//
//  The reading adopted throughout is therefore: an ordinary miss yields a default, and no
//  exception is raised. A missing key or an out-of-range index is an ordinary miss, not a
//  programming error, so throwing ArgumentOutOfRangeException or KeyNotFoundException would be a
//  behavioural change dressed as robustness - and one that would convert a legacy no-op into a
//  crash at a call site that has no handler, because the legacy had nothing to handle.
//
//  A NULL KEY IS THE ONE CASE THAT IS NOT AN ORDINARY MISS, and the boundary is drawn
//  deliberately. The key parameters are declared non-nullable `string`, so under the nullable
//  reference types enabled by Directory.Build.props null is already excluded by the contract; a
//  caller that defeats that with a null-forgiving operator receives the BCL's own
//  ArgumentNullException from the backing store. No guard is written for it. Adding one would
//  silently accept a contract violation, and it would add a branch reachable only by abusing the
//  type - which the coverage gate would then be unable to exercise honestly.
//
//  DECISION 5 - THE GLOBAL AUTO-INSTANCE AT L20 IS DELIBERATELY NOT REPRODUCED
//  --------------------------------------------------------------------------------------------
//  n_map.sru:L20 declares `global n_map n_map`: a global auto-instantiated variable whose name
//  shadows its own type name. That is legal only because PowerBuilder has one flat global
//  namespace with no import statements, where symbol resolution follows the ordering of the
//  library list in the target file. It is the same collision the migration plan records for
//  `global n_sql n_sql`, and the resolution is identical: the C# TYPE keeps the descriptive name
//  and the shared global instance is not recreated. Concretely this file declares NO static
//  field, NO static property, NO singleton, NO Instance and NO Current member.
//
//  The legacy call sites corroborate the resolution rather than merely permitting it: every one
//  of the four measured consumers instantiates its own map with `Create n_map` - at
//  n_cst_dwsvc.sru:L585, n_cst_thread_task_sqlquery.sru:L646 and
//  n_cst_thread_task_sqlbase.sru:L535 - and not one of them touches the global. A process-wide
//  shared mutable map would additionally be an outright defect here, since DECISION 7 records
//  that this type is not thread safe.
//
//  DECISION 6 - THE create AND destroy PAIR AT L22-L30 IS A DELIBERATE NON-PORT
//  --------------------------------------------------------------------------------------------
//  n_map.sru:L22-L25 and L27-L30 exist only to TriggerEvent "constructor" and "destructor", and
//  the consumers do pair their creation with an explicit teardown - `Destroy map` at
//  n_cst_thread_task_sqlquery.sru:L662 and `if IsValidObject(mapValue) then Destroy mapValue` at
//  n_cst_dwsvc_contextmenu.sru:L1076. None of that survives into the substitution, and the
//  reason is that the substituted implementation owns only managed memory: an
//  OrderedDictionary and the references it holds. There is no handle, no native allocation and
//  no unmanaged resource to release at a deterministic moment.
//
//  This file therefore implements NO IDisposable, declares NO finalizer and offers no Destroy
//  equivalent. Purge, at n_map.sru:L18, is the legacy's own way of dropping the contents and is
//  the member a caller wants; releasing the map itself is the garbage collector's business.
//  Adding disposal ceremony would publish a lifetime contract the type does not have and would
//  oblige every consumer to a using statement for no benefit.
//
//  DECISION 7 - NOT THREAD SAFE, DELIBERATELY, AND CORROBORATED BY THE LEGACY THREADING MODEL
//  --------------------------------------------------------------------------------------------
//  No lock, no concurrent collection and no synchronisation of any kind appears below, and none
//  may be added. The legacy threading model guarantees single-thread ownership by construction:
//  its concurrency classes exist as caller-side and worker-side proxy pairs precisely so that no
//  object is ever touched from two threads. The map inherits that guarantee directly at its
//  Persistence call site, where it is stored in and retrieved from PER-THREAD data -
//  `#ParentThread.of_GetData("$SQL.DataStoreCache")` at n_cst_thread_task_sqlbase.sru:L532-L534,
//  with the freshly created map published back through of_SetData at L536. Each thread owns its
//  own map instance.
//
//  Synchronisation would therefore be an unrequested addition that changes the performance and
//  allocation profile of a type two services depend on, while protecting against a sharing
//  pattern the callers do not use. The absence is documented on the type so a consumer that DOES
//  intend to share an instance knows it must supply its own coordination.
//
//  DECISION 8 - VALUES ARE STORED BY REFERENCE, AND KEYS COMPARE ORDINALLY AND CASE SENSITIVELY
//  --------------------------------------------------------------------------------------------
//  PowerBuilder `any` becomes `object?`, and a stored value is handed back as the very same
//  reference it went in as. No clone, no defensive copy and no value semantics are applied. This
//  is a hard requirement, not a preference: n_cst_thread_task_sqlbase.sru:L565 stores a
//  structure holding a live datastore-derived object and L540 reads it back to go on using it,
//  so a copy would hand the caller a detached object and break the DataStore cache outright.
//
//  Key comparison is the backing store's default for string, which is ordinal and CASE
//  SENSITIVE. This is an INTERPRETATION - the native comparison rule is not observable from the
//  repository - and it is the conservative one: PowerScript's own string equality is case
//  sensitive, and the alternative would silently merge distinct keys. It matters concretely at
//  n_cst_dwsvc.sru:L642-L648, where "Y" and "N" are registered as checkbox display keys and are
//  later matched against pasted user text at n_cst_dwsvc_contextmenu.sru:L1006. No
//  comparer-accepting constructor overload is offered, because none is requested and adding one
//  would publish a knob the legacy type does not have.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project: review_rules returns exactly "No user rules provided",
//  and that single line is the whole document. The binding constraints are therefore the
//  migration plan's own inventory, honoured here exactly as user rules would have been, and the
//  absence of rules is not treated as licence to lower the bar.
//
//  C-A  This type is shared IMPLEMENTATION consumed in-process through a ProjectReference by
//       DataServices and by Persistence. It is not a cross-service channel: no serialization
//       attribute, no DTO or protocol shaping, no gRPC or HTTP awareness, no reference to the
//       Contracts project, no logging, no configuration and no I/O of any kind. Pure behaviour.
//  C-B  Exactly the ten members of n_map.sru:L9-L18 and nothing more. Behaviour is replicated,
//       never improved: the Add-versus-Set asymmetry is preserved instead of being unified, the
//       accessors return defaults instead of gaining the failure channel the legacy lacks, and
//       the int-index-versus-ulong-count asymmetry of L11 against L17 is reproduced rather than
//       harmonised.
//  C-C  The legacy tree is read only. n_map.sru and the four consumers were read as the
//       specification and left untouched, and every behaviour cites its :L locator.
//  C-D  Nothing here reaches a deferred service. The third object in this legacy library,
//       n_list, has no in-scope consumer and is neither referenced nor named as a dependency,
//       and no XML, JSON, HTTP, UI or scripting concern appears.
//  C-H  Every member is reachable and observable from the public surface, so the coverage gate
//       can be met honestly. No unreachable defensive branch is written - see the null-key
//       boundary in DECISION 4 - and no member's behaviour depends on unobservable state.
//  C-K  Every technology-specific and boundary-specific decision is documented at its point of
//       reproduction: the backing-store substitution, the one-based contract, the Add-versus-Set
//       distinction, each INTERPRETATION, the global auto-instance resolution, the disposal
//       non-port, the thread-safety position and the comparison rule.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * IDictionary, IReadOnlyDictionary, IEnumerable and ICollection in the base list. The
//      legacy type implements no interface and exposes no enumerator, and implementing one would
//      publish foreach, LINQ and collection-initializer surfaces it never had - a far larger
//      contract than the ten prototypes, and one that would let callers bypass the one-based
//      positional contract DECISION 2 exists to protect.
//    * An indexer, TryGetValue, ContainsKey, Clear and a Count property. Each is the idiomatic
//      .NET spelling of a member that already exists here under its legacy name and shape - Get,
//      Get, Exists, Purge and Count() - and adding either spelling alongside the other would
//      create two ways to do one thing and obscure which one carries the legacy semantics.
//    * Any project reference, and in particular one to PowerFramework.Shared.Kernel. The ten
//      prototypes name nothing but string, any, int, ulong and boolean, not even a return code,
//      so the measured dependency set of this project is empty. A reference added for symmetry
//      with the other shared libraries would be a false coupling and a future cycle risk.
//    * LINQ helpers, ToDictionary, cloning, equality overrides and a ToString override. None is
//      in the legacy surface; each would be an unrequested addition.
//    * A constructor of any kind. The implicit parameterless constructor is the whole of what
//      `Create n_map` does at the three measured creation sites, and a comparer-accepting
//      overload is excluded by DECISION 8.
//    * SCREAMING_SNAKE and legacy lowercase identifiers. The plan preserves legacy constant
//      spellings only in the files that carry legacy constants, and the repository-root
//      .editorconfig scopes its naming-analyzer suppressions to the closed BAND 3 roster of file
//      globs - the single source of truth for that list - which does not include this one. With
//      TreatWarningsAsErrors inherited from
//      Directory.Build.props such an identifier here would be a build ERROR. The legacy lowercase
//      spellings are recorded in the doc comments; every identifier below is PascalCase.
// ==============================================================================================

namespace PowerFramework.Shared.Containers;

/// <summary>
/// An insertion-ordered, string-keyed map of arbitrary values with one-based positional access,
/// substituted for the legacy PowerBuilder type <c>n_map</c>
/// (<c>ws_objects/pfw.utility.container.pbl.src/n_map.sru</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a substitution onto a base class library type, not a port. <c>n_map.sru:L8</c> binds
/// the legacy type to the closed-source <c>pfw.dll</c> with <c>native "pfw.dll"</c>, and the
/// <c>.sru</c> carries the ten prototypes at <c>n_map.sru:L9-L18</c> and no function or
/// subroutine body whatsoever, so there is no PowerScript implementation to transliterate. The
/// signatures are the entire specification, and every reading that the signatures alone do not
/// settle is labelled as an interpretation on the member that reproduces it.
/// </para>
/// <para>
/// <b>Positional access is one-based.</b> The first entry is at index 1 and the last is at index
/// <see cref="Count"/>, so the valid range is <c>1 .. Count()</c> inclusive. Index 0 is never
/// valid. See <see cref="Get(int)"/> and <see cref="GetKey(int)"/>.
/// </para>
/// <para>
/// <b>Values are stored by reference and are never copied.</b> Whatever instance is handed to
/// <see cref="Add"/> or <see cref="Set"/> is the instance <see cref="Get(string)"/> returns.
/// Live objects are genuinely stored this way in the legacy code
/// (<c>n_cst_thread_task_sqlbase.sru:L565</c> stores a structure holding a live datastore and
/// <c>:L540</c> reads it back), so no cloning or value semantics may be introduced.
/// </para>
/// <para>
/// <b>Keys are compared ordinally and case sensitively</b>, matching PowerScript's own string
/// equality. This is an interpretation: the native comparison rule is not observable from the
/// repository. No comparer-accepting constructor is offered.
/// </para>
/// <para>
/// <b>This type is not thread safe and deliberately performs no synchronisation.</b> The legacy
/// threading model gives each thread its own instance - the Persistence call site keeps the map
/// in per-thread data at <c>n_cst_thread_task_sqlbase.sru:L532-L536</c> - so a caller that
/// chooses to share one instance across threads must supply its own coordination.
/// </para>
/// <para>
/// <b>No member throws for an ordinary miss.</b> A missing key or an out-of-range index yields a
/// default, because the legacy accessors at <c>n_map.sru:L10-L12</c> have no failure channel at
/// all. A null key is the one exception, and it is a contract violation rather than a miss: the
/// key parameters are non-nullable, and defeating that yields the backing store's own
/// <see cref="ArgumentNullException"/>.
/// </para>
/// <para>
/// <c>n_map.sru:L20</c> declares <c>global n_map n_map</c>, a global auto-instance whose name
/// shadows its own type name. That instance is deliberately not reproduced: this type exposes no
/// static or singleton member and is constructed per use, exactly as the legacy consumers do with
/// <c>Create n_map</c>. The class is sealed because the legacy type is a closed native binding
/// with no in-scope derivation.
/// </para>
/// </remarks>
public sealed class OrderedMap
{
    /// <summary>
    /// The backing store: the one base class library type that supplies insertion order and
    /// positional access together, which is exactly the pair <c>get(int)</c>, <c>getkey(int)</c>
    /// and <c>getkeys</c> require.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="Dictionary{TKey, TValue}"/> is deliberately NOT used here even though it
    /// would satisfy the four key-based members: its enumeration order is unspecified and is
    /// perturbed by removal and regrowth, so the three positional members would silently return
    /// correct values in the wrong order. <see cref="OrderedDictionary{TKey, TValue}"/> ships in
    /// the <c>net10.0</c> framework reference, so this project needs no package reference, and
    /// <c>System.Collections.Generic</c> arrives through the repository-wide implicit usings, so
    /// this file needs no using directive.
    /// </remarks>
    private readonly OrderedDictionary<string, object?> _entries = new();

    /// <summary>
    /// Converts a caller-supplied ONE-BASED index into the zero-based slot of the backing store,
    /// or returns a negative sentinel when the index falls outside <c>1 .. Count()</c>.
    /// </summary>
    /// <param name="oneBasedIndex">
    /// The one-based index a caller passed to <see cref="Get(int)"/> or <see cref="GetKey(int)"/>.
    /// </param>
    /// <returns>
    /// The zero-based slot when <paramref name="oneBasedIndex"/> is within
    /// <c>1 .. Count()</c> inclusive; otherwise <c>-1</c>, following the same negative-sentinel
    /// convention the base class library uses for <c>IndexOf</c>. Callers test the result with
    /// <c>&lt; 0</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is the ONLY place in this file that subtracts one from an index, and every
    /// positional member routes through it. That centralisation is deliberate: translating a
    /// one-based position to a zero-based slot is the most dangerous mechanical hazard in this
    /// migration, because an off-by-one returns a real neighbouring entry rather than failing, so
    /// the arithmetic is written once and validated once instead of repeated at each call site.
    /// </para>
    /// <para>
    /// The two range tests are ordered deliberately and must not be reordered or merged. The
    /// <c>&lt; 1</c> test runs FIRST so that the conversion to the unsigned width of
    /// <see cref="Count"/> is only ever applied to a positive value; were the comparison made
    /// the other way round, a negative <see cref="int"/> would reinterpret as an enormous
    /// unsigned value and sail past an upper-bound check. This is the concrete handling of the
    /// signed-index against unsigned-count asymmetry the legacy declares at
    /// <c>n_map.sru:L11-L12</c> versus <c>n_map.sru:L17</c>, which is reproduced rather than
    /// harmonised away.
    /// </para>
    /// <para>
    /// The guard is also what keeps the no-throw contract of the accessors true: the backing
    /// store's positional accessor raises
    /// <see cref="ArgumentOutOfRangeException"/> for a slot it does not hold, and this validation
    /// is what stops such a slot from ever reaching it.
    /// </para>
    /// </remarks>
    private int ToZeroBasedSlot(int oneBasedIndex)
    {
        // Reject zero and every negative index BEFORE widening, so int.MinValue can never be
        // reinterpreted as a huge unsigned value by the upper-bound test below.
        if (oneBasedIndex < 1)
        {
            return -1;
        }

        // Upper bound expressed against the public Count(), so the documented valid range
        // "1 .. Count() inclusive" is literally the range enforced here.
        if ((ulong)oneBasedIndex > Count())
        {
            return -1;
        }

        // The single one-based-to-zero-based conversion in this file.
        return oneBasedIndex - 1;
    }

    /// <summary>
    /// Adds an entry at the end of the insertion order, refusing a key that is already present.
    /// </summary>
    /// <param name="key">The key to add. Compared ordinally and case sensitively.</param>
    /// <param name="value">
    /// The value to associate with <paramref name="key"/>. May be <see langword="null"/>, and is
    /// stored by reference exactly as supplied.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the entry was added; <see langword="false"/> when
    /// <paramref name="key"/> was already present, in which case the map is left completely
    /// unchanged and the existing value is retained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L9  public function boolean add(string key, any value)</c>.
    /// </para>
    /// <para>
    /// This is the member that refuses a duplicate; <see cref="Set"/> is the member that accepts
    /// one. Keeping the two distinct reproduces the legacy surface, which declares them as two
    /// separate prototypes with identical parameter lists and identical return types. Both
    /// measured call sites guard with <see cref="Exists"/> before calling this method
    /// (<c>n_cst_thread_task_sqlquery.sru:L652</c> then <c>:L658</c>, and
    /// <c>n_cst_thread_task_sqlbase.sru:L539</c> then <c>:L565</c>), which is consistent with a
    /// refusal on duplicate but does not by itself prove the returned value, so
    /// <see langword="false"/> on a duplicate is an interpretation of the boolean return.
    /// </para>
    /// <para>
    /// A new entry is appended, so it becomes entry <see cref="Count"/> under the one-based
    /// positional contract. Existing entries keep their positions.
    /// </para>
    /// </remarks>
    public bool Add(string key, object? value)
    {
        // Refuses a duplicate WITHOUT mutating the existing entry, which is precisely the
        // Add-versus-Set asymmetry the two legacy prototypes describe.
        return _entries.TryAdd(key, value);
    }

    /// <summary>
    /// Returns the value stored under <paramref name="key"/>, or <see langword="null"/> when no
    /// such entry exists.
    /// </summary>
    /// <param name="key">The key to look up. Compared ordinally and case sensitively.</param>
    /// <returns>
    /// The stored value, which may itself be <see langword="null"/>; or <see langword="null"/>
    /// when <paramref name="key"/> is absent. Use <see cref="Exists"/> to tell those two cases
    /// apart.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L10  public function any get(string key)</c>.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see langword="null"/> for a missing key. The legacy signature
    /// returns a bare <c>any</c> with no boolean out-parameter and no return code, so it has no
    /// channel through which to report a miss, and every measured call site guards with
    /// <see cref="Exists"/> first (<c>n_cst_dwsvc_contextmenu.sru:L1006</c> then <c>:L1007</c>,
    /// and <c>n_cst_thread_task_sqlbase.sru:L539</c> then <c>:L540</c>). Yielding the default is
    /// therefore the reading adopted, and no exception is raised for a miss.
    /// </para>
    /// <para>
    /// A consequence worth stating because it cannot be designed away: a stored
    /// <see langword="null"/> and an absent key are indistinguishable through this member alone.
    /// That ambiguity is inherent to a signature with no failure channel and is not introduced
    /// here; <see cref="Exists"/> is the discriminator.
    /// </para>
    /// <para>
    /// The value is returned as the same reference that was stored. No copy is made, which is
    /// what allows the legacy DataStore cache at <c>n_cst_thread_task_sqlbase.sru:L540</c> to go
    /// on using the live object it retrieves.
    /// </para>
    /// </remarks>
    public object? Get(string key)
    {
        // A miss yields the default rather than throwing: the legacy signature has no failure
        // channel. A stored null is reported identically; Exists() discriminates.
        return _entries.TryGetValue(key, out object? value) ? value : null;
    }

    /// <summary>
    /// Returns the value of the entry at the given ONE-BASED position, or <see langword="null"/>
    /// when the position is out of range. The first entry is at index 1 and the last is at index
    /// <see cref="Count"/>.
    /// </summary>
    /// <param name="index">
    /// The one-based position. Valid values run from 1 to <see cref="Count"/> inclusive; 0 is
    /// never valid.
    /// </param>
    /// <returns>
    /// The value at that position, which may itself be <see langword="null"/>; or
    /// <see langword="null"/> when <paramref name="index"/> lies outside
    /// <c>1 .. Count()</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L11  public function any get(int index)</c>.
    /// </para>
    /// <para>
    /// <b>This overload is ONE-BASED.</b> A ported call site that assumes the zero-based
    /// convention of C# collections will read the wrong entry on every call and will do so
    /// silently, because it receives a real neighbouring value rather than an error. The
    /// conversion and the range validation both happen in one private helper, and the parameter
    /// stays a signed <see cref="int"/> - matching the legacy declaration - even though
    /// <see cref="Count"/> returns an unsigned width, an asymmetry reproduced rather than tidied.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see langword="null"/> for an out-of-range index, on the same
    /// grounds as <see cref="Get(string)"/>: the signature offers no failure channel. No
    /// exception is raised for any index, including 0 and negative values.
    /// </para>
    /// <para>
    /// This overload addresses exactly the same entry as <see cref="GetKey(int)"/> for any given
    /// index, so <c>Get(i)</c> is always the value belonging to the key <c>GetKey(i)</c> returns.
    /// </para>
    /// </remarks>
    public object? Get(int index)
    {
        int slot = ToZeroBasedSlot(index);

        // Out of range yields the default; the guard is also what keeps the backing store's
        // positional accessor from raising for a slot it does not hold.
        return slot < 0 ? null : _entries.GetAt(slot).Value;
    }

    /// <summary>
    /// Returns the key of the entry at the given ONE-BASED position, or
    /// <see cref="string.Empty"/> when the position is out of range. The first entry is at index
    /// 1 and the last is at index <see cref="Count"/>.
    /// </summary>
    /// <param name="index">
    /// The one-based position. Valid values run from 1 to <see cref="Count"/> inclusive; 0 is
    /// never valid.
    /// </param>
    /// <returns>
    /// The key at that position; or <see cref="string.Empty"/> when <paramref name="index"/> lies
    /// outside <c>1 .. Count()</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L12  public function string getkey(int index)</c>.
    /// </para>
    /// <para>
    /// <b>This overload is ONE-BASED</b>, on the same terms as <see cref="Get(int)"/>.
    /// <c>GetKey(Count())</c> returns the last key, which is the property that fails loudly if
    /// the implementation ever drifts to zero-based indexing.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see cref="string.Empty"/> rather than
    /// <see langword="null"/> for an out-of-range index. The legacy return type is a bare
    /// <c>string</c> with no failure channel, so a default must be yielded; the empty string is
    /// chosen over null so the declared non-nullable return stays honest under the nullable
    /// reference types this repository enables, and so a caller can concatenate the result
    /// without a null check. No exception is raised for any index.
    /// </para>
    /// </remarks>
    public string GetKey(int index)
    {
        int slot = ToZeroBasedSlot(index);

        // Out of range yields the empty string, keeping the non-nullable return type honest.
        return slot < 0 ? string.Empty : _entries.GetAt(slot).Key;
    }

    /// <summary>
    /// Replaces <paramref name="keys"/> with a newly allocated array holding every key in
    /// insertion order.
    /// </summary>
    /// <param name="keys">
    /// Passed by reference and always overwritten with a fresh array; any value the caller
    /// supplied is discarded rather than appended to. The array is ZERO-based, so logical entry 1
    /// lands at <c>keys[0]</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> always, including for an empty map, in which case
    /// <paramref name="keys"/> receives a zero-length array.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart
    /// <c>n_map.sru:L13  public function boolean getkeys(ref string keys[])</c>.
    /// </para>
    /// <para>
    /// The parameter is <see langword="ref"/> rather than <see langword="out"/> because the
    /// legacy parameter is a PowerBuilder <c>ref</c>, which is an in-out parameter, and the
    /// migration plan's type mapping is explicit on the point. It is deliberately not
    /// "improved" into an <see langword="out"/> parameter or a returned array.
    /// </para>
    /// <para>
    /// <b>This is the second one-based-to-zero-based conversion point in this file</b>, and the
    /// only one outside the private index helper. It is a change of representation rather than a
    /// change of order: the sequence handed back is identical to
    /// <c>GetKey(1)</c>, <c>GetKey(2)</c>, ... <c>GetKey(Count())</c>, but it is delivered in a
    /// zero-based C# array, so the entry at one-based position <c>i</c> is found at
    /// <c>keys[i - 1]</c>.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see langword="true"/> unconditionally, including on an empty
    /// map. The operation succeeded and produced the complete set of keys, which happens to be
    /// empty; reporting failure would conflate "nothing to give" with "could not give it". This
    /// member has no measured call site anywhere in the repository, so nothing in the legacy code
    /// constrains the returned value.
    /// </para>
    /// </remarks>
    public bool GetKeys(ref string[] keys)
    {
        // Always a fresh array so the caller cannot mutate the map's own ordering through it,
        // and so the ref parameter's prior contents are never partially retained.
        string[] result = new string[_entries.Count];

        // Insertion order, and the representation shift from one-based logical positions to a
        // zero-based array: logical entry 1 lands at result[0].
        _entries.Keys.CopyTo(result, 0);
        keys = result;

        return true;
    }

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, overwriting an existing
    /// entry in place without moving it, or appending a new entry when the key is absent.
    /// </summary>
    /// <param name="key">The key to assign. Compared ordinally and case sensitively.</param>
    /// <param name="value">
    /// The value to associate with <paramref name="key"/>. May be <see langword="null"/>, and is
    /// stored by reference exactly as supplied.
    /// </param>
    /// <returns><see langword="true"/> always.</returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L14  public function boolean set(string key, any value)</c>.
    /// </para>
    /// <para>
    /// This is the member that accepts a key already present; <see cref="Add"/> is the member that
    /// refuses one. An overwrite <b>keeps the entry's existing position</b> in the insertion
    /// order, so the one-based positional view is unaffected by a value change; only a genuinely
    /// new key extends the sequence, and it extends it at the end.
    /// </para>
    /// <para>
    /// That this member INSERTS when the key is absent is corroborated by call-site behaviour
    /// rather than assumed. <c>n_cst_dwsvc.sru:L585</c> creates an empty map,
    /// <c>:L634</c> and <c>:L642-L655</c> populate it through this member alone and never through
    /// <see cref="Add"/>, <c>:L663</c> returns it, and
    /// <c>n_cst_dwsvc_contextmenu.sru:L1006-L1007</c> then reads entries back out of it. Were
    /// this member not to insert, that map would always be empty and the feature built on it
    /// would be unreachable code.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see langword="true"/>. None of the seven measured call sites
    /// inspects the returned value, so while the insert-and-overwrite behaviour is corroborated,
    /// the boolean result is not constrained by any legacy code. Reporting success
    /// unconditionally is the reading adopted, since the assignment cannot partially fail.
    /// </para>
    /// </remarks>
    public bool Set(string key, object? value)
    {
        // Assignment through the backing store's indexer gives exactly the two behaviours the
        // legacy consumers rely on: an existing key is overwritten WITHOUT changing its position
        // in the insertion order, and an absent key is appended at the end.
        _entries[key] = value;

        return true;
    }

    /// <summary>
    /// Reports whether an entry with the given key is present.
    /// </summary>
    /// <param name="key">The key to test. Compared ordinally and case sensitively.</param>
    /// <returns>
    /// <see langword="true"/> when an entry with that key exists, even if its stored value is
    /// <see langword="null"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L15  public function boolean exists(string key)</c>.
    /// </para>
    /// <para>
    /// This member is the failure channel the accessors lack, and the legacy code uses it that
    /// way: it is the guard in front of every measured read and every measured add
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1006</c>,
    /// <c>n_cst_thread_task_sqlquery.sru:L652</c> and
    /// <c>n_cst_thread_task_sqlbase.sru:L539</c>). It is therefore also the only way to
    /// distinguish an entry whose stored value is <see langword="null"/> from a key that is
    /// absent, since <see cref="Get(string)"/> returns <see langword="null"/> for both.
    /// </para>
    /// </remarks>
    public bool Exists(string key)
    {
        return _entries.ContainsKey(key);
    }

    /// <summary>
    /// Removes the entry with the given key, leaving the relative order of every surviving entry
    /// unchanged.
    /// </summary>
    /// <param name="key">The key to remove. Compared ordinally and case sensitively.</param>
    /// <returns>
    /// <see langword="true"/> when an entry was removed; <see langword="false"/> when
    /// <paramref name="key"/> was absent, in which case the map is left unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L16  public function boolean remove(string key)</c>.
    /// </para>
    /// <para>
    /// Survivors keep their relative order, so the one-based positional view stays coherent after
    /// a removal: it simply becomes one entry shorter, and every entry that followed the removed
    /// one shifts down by a single position. <see cref="Count"/> reflects the removal immediately,
    /// which is what keeps the <c>1 .. Count()</c> range valid.
    /// </para>
    /// <para>
    /// INTERPRETATION - returning <see langword="false"/> for an absent key rather than raising.
    /// A removal that finds nothing to remove is an ordinary miss, and this member has no measured
    /// call site anywhere in the repository, so nothing in the legacy code constrains the result.
    /// Discriminating hit from miss through the boolean return is the natural reading of a
    /// <c>boolean</c>-returning removal.
    /// </para>
    /// </remarks>
    public bool Remove(string key)
    {
        return _entries.Remove(key);
    }

    /// <summary>
    /// Returns the number of entries, which is also the highest valid one-based position.
    /// </summary>
    /// <returns>The entry count. Zero for an empty map.</returns>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L17  public function ulong count()</c>.
    /// </para>
    /// <para>
    /// This is a METHOD returning an unsigned width, not a <c>Count</c> property returning
    /// <see cref="int"/>, because the legacy declaration is a function returning <c>ulong</c> and
    /// the surface is kept a faithful mirror. The signed-index against unsigned-count asymmetry
    /// against <c>n_map.sru:L11-L12</c> is genuinely in the legacy declarations and is reproduced
    /// rather than harmonised; the private index helper is where that asymmetry is handled
    /// safely.
    /// </para>
    /// <para>
    /// The widening conversion below is always safe because the backing store's own count is a
    /// non-negative <see cref="int"/>.
    /// </para>
    /// </remarks>
    public ulong Count()
    {
        // Always safe: the backing store's count is a non-negative int.
        return (ulong)_entries.Count;
    }

    /// <summary>
    /// Removes every entry, leaving the map empty and reusable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy counterpart <c>n_map.sru:L18  public subroutine purge()</c>.
    /// </para>
    /// <para>
    /// Named <c>Purge</c> rather than <c>Clear</c>, and returning nothing, because the legacy
    /// member is a subroutine with that name; the idiomatic .NET spelling is deliberately not
    /// added alongside it, which would give one operation two names and obscure which carries the
    /// legacy semantics.
    /// </para>
    /// <para>
    /// This is the legacy's own way of dropping the contents, and it is the reason no disposal
    /// pattern is implemented on this type. The instance stays fully usable afterwards:
    /// <see cref="Count"/> returns zero, and the next <see cref="Add"/> or <see cref="Set"/>
    /// starts a fresh insertion order at position 1. Purging an already empty map is a no-op and
    /// does not raise.
    /// </para>
    /// </remarks>
    public void Purge()
    {
        _entries.Clear();
    }
}
