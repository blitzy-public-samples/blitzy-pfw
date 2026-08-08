// ==============================================================================================
//  Ancestry - the PowerFramework type-ancestry predicates
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    three global function objects under ws_objects/pfw.common.pbl.src/, whose
//                 provenance is NOT uniform and matters for how each is read:
//
//                     isancestor.srf          11 lines   2 prototypes   NATIVE, body in pfw.dll
//                     isancestorbyclass.srf   23 lines   1 prototype    pure PowerScript
//                     isancestorbyobject.srf  23 lines   1 prototype    pure PowerScript
//
//                 Four legacy entry points become the four members of this class, one for one.
//                 Nothing is collapsed and nothing is added; DECISION 6 records the one member
//                 that was considered and deliberately left out.
//
//  ORACLE STATUS  Those three .srf files are the ONLY specification for this behaviour and they
//                 are READ ONLY: they are the behavioural oracle for parity testing, never an
//                 edit target. Every member below carries the `<file>.srf:L` locator of the line
//                 it reproduces, and every body quotes the PowerScript it stands in for, because
//                 nothing outside ws_objects can adjudicate a disagreement about what these
//                 predicates answer. A reader who doubts a body should read the locator rather
//                 than reason from first principles - reasoning from first principles is exactly
//                 how the self-inclusive walk recorded below gets "fixed" into something else.
//
//  ONE OF THE THREE HAS NO READABLE BODY, AND THAT SHAPES THE WHOLE FILE
//  --------------------------------------------------------------------------------------------
//  isancestor.srf:L3 is `global type isancestor from function_object native "pfw.dll"`. It is a
//  PBNI binding: the declaration is all there is. The two prototypes at L7 and L8 are the entire
//  readable surface, there is no `end function` anywhere in the file, and no C++ source for
//  pfw.dll exists anywhere in the repository. Its behaviour therefore cannot be read, only
//  INFERRED - and the inference is unusually well grounded, because the two managed siblings are
//  a prototype-for-prototype mirror of it:
//
//      isancestor.srf:L7   (readonly powerobject object, readonly string parentcls)
//      isancestorbyobject.srf:L7                    the same shape, with a readable body
//
//      isancestor.srf:L8   (readonly string cls, readonly string parentcls)
//      isancestorbyclass.srf:L7                     the same shape, with a readable body
//
//  The two readable bodies are consequently treated as the best available specification for the
//  unreadable one, which is why the two IsAncestor overloads DELEGATE rather than reimplement.
//  DECISION 5 states that in full, including why delegation is a correctness property here and
//  not a convenience.
//
//  THE ONE THING TO UNDERSTAND BEFORE CHANGING ANYTHING IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  THE ANCESTOR WALK IS SELF-INCLUSIVE. A TYPE IS ITS OWN ANCESTOR.
//
//  This is the single most important behaviour in the file and the one a "correct" implementation
//  is most likely to break, because the everyday meaning of "ancestor" excludes the thing itself.
//  The oracle is unambiguous. isancestorbyclass.srf:L14-L18 reads:
//
//      clsDef = FindClassDefinition(cls)      <-- seeded with the class ITSELF
//      do while IsValid(clsDef)
//          if clsDef.name=parentCls then return true
//          clsDef = clsDef.Ancestor           <-- the advance happens AFTER the comparison
//      loop
//
//  The comparison precedes the advance, so the seed is tested before any base type is. Therefore
//  `IsAncestorByClass("Foo", "Foo")` is TRUE, and `IsAncestorByObject(new Foo(), "Foo")` is TRUE.
//  isancestorbyobject.srf:L14-L18 is the identical loop with a different seed, so both share the
//  property.
//
//  This is not an accident of the loop shape either - it is what makes the 30 real call sites
//  work. Every one of them is a dispatch test of the form "is this thing a <T>", for example
//  `IsAncestor(target,"n_cst_dwsvc")` at
//  ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L603, and an EXACT n_cst_dwsvc must
//  answer yes there just as a subclass of it must. Narrowing the walk to proper-ancestor-only
//  would silently turn every one of those tests into "is this thing a STRICT subclass of <T>",
//  which is a different question with a different answer for the most common input.
//
//  DECISION 1 - ClassDefinition BECOMES System.Type, AND .Ancestor BECOMES Type.BaseType
//  --------------------------------------------------------------------------------------------
//  The oracle walks PowerBuilder's own reflection model: a `ClassDefinition` handle obtained from
//  `FindClassDefinition` or from an object's `ClassDefinition` property, advanced through its
//  `Ancestor` property, with `IsValid` as the loop's termination test.
//
//  The substitution is the one the refactor's native-substitution matrix prescribes for the
//  ancestry primitives, mapped member for member:
//
//      ClassDefinition                 System.Type
//      object.ClassDefinition          object.GetType()
//      clsDef.Ancestor                 type.BaseType
//      clsDef.name                     type.Name                 (see DECISION 2)
//      IsValid(clsDef)                 type is not null
//      FindClassDefinition(cls)        a name lookup             (see DECISION 4)
//
//  The termination tests correspond exactly rather than approximately: `Ancestor` on a root class
//  yields an invalid handle, and `BaseType` on a root type yields null, so both loops end for the
//  same structural reason. There is one observable consequence of the substitution, recorded in
//  the behaviour table below and worth naming here: the .NET chain terminates at System.Object,
//  so the name "Object" is in EVERY class's chain and matches everything. The legacy chain
//  terminated at PowerBuilder's own root instead, so the specific name that matches everything
//  differs. The SHAPE is preserved; the root's spelling is a platform fact, not a choice.
//
//  DECISION 2 - THE COMPARISON IS ON THE UNQUALIFIED NAME, AND THAT HAS A CONSEQUENCE
//  --------------------------------------------------------------------------------------------
//  isancestorbyclass.srf:L16 and isancestorbyobject.srf:L16 both read `if clsDef.name=parentCls`.
//  `ClassDefinition.name` is a bare class name, because PowerBuilder has ONE FLAT GLOBAL
//  NAMESPACE with no namespaces at all - symbol resolution is by the ordering of the library list
//  in the target file, not by qualification. The C# comparison is therefore against `Type.Name`,
//  and it must NOT be `Type.FullName` and must NOT be `Type.AssemblyQualifiedName`.
//
//  Upgrading to FullName is the most tempting change in this file, because FullName is "more
//  correct" in isolation. It would be a behavioural change, and a total one: every real call site
//  passes a bare legacy class name - `"n_cst_dwsvc"`, `"olecustomcontrol"`, `"userobject"`,
//  `"menucascade"`, `"n_cst_button_theme"` - and not one of them could ever match a namespaced
//  FullName. The predicate would answer false everywhere. Type.Name is also already the module's
//  established reading of the same question: Text.ClassNameEx returns `value.GetType().Name` for
//  exactly this reason, and its own decision block spells the argument out.
//
//  THE HONEST CONSEQUENCE, WRITTEN DOWN RATHER THAN LEFT TO BE DISCOVERED. The .NET tree
//  introduces namespaces where the legacy had none, so two DISTINCT types in two different
//  namespaces can share an unqualified name. Under an unqualified comparison BOTH satisfy this
//  predicate against that name: a chain containing Alpha.Widget and a chain containing
//  Beta.Widget both answer true to `"Widget"`. That is not reachable in the legacy - a flat
//  namespace cannot hold two Widgets - so there is no legacy answer being contradicted, and it is
//  the faithful and sanctioned reading of an unqualified comparison. It is pinned by a test
//  precisely so that nobody later reads it as a defect and "fixes" it to FullName.
//
//  The comparison is ORDINAL, via StringComparison.Ordinal. PowerScript `=` on strings is a
//  byte-for-byte comparison, so ordinal is the faithful match; a culture-sensitive comparison
//  would additionally make the answer depend on the ambient culture and so break the
//  reproducibility that characterization recordings depend on. The classic demonstration is the
//  Turkish dotless i, under which a culture-sensitive comparison of "I" and "i" can change
//  answer; the ordinal comparison here cannot, and a test asserts that under tr-TR.
//
//  DECISION 3 - BASE TYPES ONLY. INTERFACES ARE NOT WALKED, AND IsAssignableFrom IS NOT USABLE
//  --------------------------------------------------------------------------------------------
//  A reader who knows `Type.IsAssignableFrom` will wonder why it is not used, so the reasoning is
//  stated rather than left implicit. There are two independent reasons, and either alone settles
//  it.
//
//  FIRST, IT DOES NOT FIT THE INPUTS. IsAssignableFrom needs the target as a resolved Type. Here
//  the target arrives only ever as a runtime STRING. That is not a theoretical shape: two call
//  sites pass a constant rather than a literal, and
//  ws_objects/pfw.ui.controls.pbl.src/u_cst_tabcontrol.sru passes an ARRAY ELEMENT,
//  `IsAncestor(control,strValidCls[index])`, inside a loop over candidate class names. A name is
//  not a type and cannot be turned into one without the lookup of DECISION 4, whose result may be
//  a DIFFERENT type that merely shares the name. So for the object-shaped overload - the only one
//  with any call sites at all - a name walk is not the convenient implementation, it is the only
//  correct one.
//
//  SECOND, IT WOULD ANSWER A WIDER QUESTION. IsAssignableFrom is true across implemented
//  interfaces, and the oracle's chain cannot contain one: `ClassDefinition.Ancestor` is single
//  inheritance, and PowerBuilder has no interfaces in this sense for it to traverse. Walking
//  interfaces would make this predicate answer true for relationships the legacy has no way to
//  express, which is a widening, not added fidelity. The walk is therefore strictly
//  `Type.BaseType`.
//
//  A .NET-only corollary follows and is called out so it is not mistaken for a defect: because
//  `typeof(ISomething).BaseType` is null, an INTERFACE resolved as the class argument has a chain
//  of exactly one entry, itself. `IsAncestorByClass("IDisposable", "IDisposable")` is true by the
//  self-inclusive rule, while `IsAncestorByClass("IDisposable", "Object")` is FALSE - an interface
//  does not reach System.Object through BaseType. The legacy had no interfaces and so no
//  behaviour here to preserve either way.
//
//  DECISION 4 - THE FindClassDefinition SUBSTITUTION, ITS SCOPE, ITS TIE-BREAK AND ITS LIMIT
//  --------------------------------------------------------------------------------------------
//  isancestorbyclass.srf:L14 is `clsDef = FindClassDefinition(cls)`. In PowerBuilder that always
//  succeeds or fails unambiguously, because the flat global namespace holds at most one class of
//  any given name and the library list decides which library supplies it. .NET has no flat
//  namespace to search, so a scope has to be CHOSEN, and choosing it silently would be the wrong
//  kind of decision to leave undocumented.
//
//  THE SCOPE IS THE ALREADY-LOADED ASSEMBLIES OF THE CURRENT AppDomain, and nothing else.
//  `AppDomain.CurrentDomain.GetAssemblies()` reports what is already loaded; it does not load
//  anything. Deliberately NOT used, and none may be introduced: Assembly.LoadFrom, Assembly.
//  LoadFile, Assembly.Load, any probing of a directory, and any other filesystem access. This
//  library is pure behaviour with no I/O - that is constraint C-A and the project file states it
//  as a property of the whole assembly - so a disk touch here would breach the boundary that
//  keeps this project at the bottom of the dependency graph. Dynamic assemblies are skipped: they
//  are not part of any flat-namespace analogue, and enumerating them is not universally
//  supported.
//
//  THE TIE-BREAK IS DETERMINISTIC, AND THAT IS THE REASON IT EXISTS. Because namespaces are new,
//  two loaded types can share an unqualified name where the legacy could hold only one - so a
//  tie-break is a situation with NO legacy answer, which this file must therefore define rather
//  than inherit. The rule is: among every ordinal Type.Name match, the winner is the one whose
//  Type.FullName is ordinally smallest. The obvious alternative - stop at the first match found -
//  is REJECTED, because neither the order of AppDomain.CurrentDomain.GetAssemblies() nor the
//  order of Assembly.GetTypes() is guaranteed by the runtime, so "first" would make the answer
//  depend on load order and the same input could answer differently across two runs of the same
//  program. That is precisely the non-determinism a characterization recording cannot tolerate.
//  The scan therefore runs to completion instead of exiting early, and the early exit is given up
//  on purpose. No performance claim is made or implied by that trade, in either direction; the
//  repository publishes no latency or throughput target of any kind, so correctness and
//  reproducibility are the only criteria available to decide it.
//
//  EXACTLY ONE TYPE IS RESOLVED AND ONLY THAT ONE IS WALKED. This mirrors the oracle, whose
//  FindClassDefinition yields a single ClassDefinition handle. The consequence in the collision
//  case is worth being explicit about: if Alpha.Widget derives from Base and Beta.Widget does
//  not, then `IsAncestorByClass("Widget", "Base")` answers for whichever Widget the tie-break
//  selects, not for both. Answering "true if ANY same-named type qualifies" was considered and
//  rejected, because it would replace a single resolution with a search and so widen a contract
//  that the oracle states in the singular.
//
//  THE HONEST LIMITATION. A type in an assembly that has not been loaded yet WILL NOT BE FOUND,
//  where the legacy - whose libraries are all listed up front - would have found it. .NET loads
//  assemblies lazily, so the resolvable set genuinely grows over the life of a process. This is
//  an unavoidable consequence of replacing a flat namespace with assemblies and is recorded here
//  rather than presented as equivalence. It has no effect on the object-shaped overloads, which
//  never resolve a name and are the only ones with call sites; and the failure mode is the benign
//  one, a false rather than a throw.
//
//  NO CACHE, DELIBERATELY. Memoizing the lookup would make this class stateful, which C-A's pure-
//  function posture rules out, and it would go stale in exactly the situation that matters: a
//  type in an assembly loaded after the cache was populated would stay unresolvable for the rest
//  of the process. A live scan is the only form that stays correct as the loaded set grows.
//
//  DECISION 5 - THE TWO IsAncestor OVERLOADS DELEGATE, AND DELEGATION IS THE POINT
//  --------------------------------------------------------------------------------------------
//  isancestor.srf has no readable body, so there is nothing to transcribe. What there IS, is two
//  prototypes that mirror the two readable siblings exactly, which makes those siblings the best
//  available specification for it. This file therefore implements the two overloads as one-line
//  delegations to IsAncestorByObject and IsAncestorByClass.
//
//  Delegation rather than duplication is a correctness property here, not a tidiness one. The
//  native function and the managed fallbacks are asserted to agree; if the overloads carried
//  their own copy of the walk, that assertion would be a claim maintained by hand, and a future
//  edit to one body could make the inferred semantics and the specification they were inferred
//  FROM diverge without anything reporting it. With delegation the two cannot diverge even in
//  principle. A test asserts the agreement across the whole hierarchy anyway, so the property is
//  pinned from both directions.
//
//  What is NOT claimed: that the native body is known to do this. It is not. The inference is
//  recorded as an inference, at the point of reproduction, on both overloads.
//
//  ONE OVERLOAD-RESOLUTION FACT THAT SURPRISES EVERYONE, so it is stated rather than discovered.
//  `IsAncestor(null, "X")` binds to the STRING overload, not the object one, because string is
//  the more specific parameter type and a bare null literal converts to both. The answer is false
//  either way - both guards reject it - so no caller is misled about the result, but a reader
//  stepping through a debugger should know which member they are in.
//
//  DECISION 6 - AN IsAncestor(Type, string) OVERLOAD WAS CONSIDERED AND IS DELIBERATELY ABSENT
//  --------------------------------------------------------------------------------------------
//  Offering `IsAncestor(Type? type, string parentClass)` is an obvious convenience: a .NET caller
//  often has a Type in hand, and routing it through a name string is a detour. It is deliberately
//  NOT offered, for three reasons, and it is recorded here so its absence reads as a decision
//  rather than an omission and so nobody adds it back without meeting them.
//
//  1. IT WOULD BREAK EXISTING CALL SITES AT COMPILE TIME. This was measured, not assumed. With
//     the object and string overloads alone, `IsAncestor(null, "X")` compiles and binds to the
//     string overload. Adding a Type overload makes that same expression fail outright, because a
//     null literal converts to string and to Type equally well and neither is more specific than
//     the other:
//
//         error CS0121: The call is ambiguous between the following methods or properties:
//         'A.F(string?, string)' and 'A.F(System.Type?, string)'
//
//     A convenience overload that turns legal call sites into build failures is not a convenience.
//
//  2. IT WOULD MAKE THE ANSWER DEPEND ON A VARIABLE'S STATIC TYPE. A Type instance passed to the
//     object overload is walked as what it is at runtime, so its chain is the runtime type's own.
//     Passed to a Type overload it would instead be walked as what it REPRESENTS. Same value,
//     same argument position, two different questions, selected silently by the declared type of
//     the variable holding it. That is a footgun, and it has no counterpart in the oracle.
//
//  3. NOTHING ASKS FOR IT. Measured across ws_objects: all 30 real call sites pass an OBJECT and
//     a NAME. The string-shaped prototype at isancestor.srf:L8 has ZERO call sites, as do
//     isancestorbyclass and isancestorbyobject. A Type-shaped entry point would be the fourth
//     unused spelling of a function that is used exactly one way, and constraint C-B's direction
//     on surface beyond the legacy is to prefer not to add it.
//
//  For the same reason there is no generic IsAncestor<T>, no IsAncestorOf inverse, no
//  IsProperAncestor and no params array of candidate names. The surface is the legacy's four
//  entry points and nothing else.
//
//  DECISION 7 - Predicates.IsValidObject IS CONSUMED, NOT REIMPLEMENTED
//  --------------------------------------------------------------------------------------------
//  isancestorbyobject.srf:L12 guards with `Not IsValidObject(object)`. That is a call to another
//  ported global function, which lives in Predicates in this same project and namespace, so the
//  port calls it: `!Predicates.IsValidObject(value)`.
//
//  An inline `value is null` would compile to the same thing TODAY and is still wrong to write.
//  IsValidObject carries a documented substitution of its own - it stands in for PowerBuilder's
//  IsValid, which reports whether a reference is live rather than merely non-null, and its
//  DECISION 7 records that .NET disposal is deliberately not consulted along with the residual
//  gap that leaves. Duplicating the check here would fork that decision into two places and let
//  them drift, so that a future refinement of what "valid" means would silently stop applying to
//  the ancestry predicates. The call is the dependency edge the oracle actually states, and a
//  test asserts the two stay coupled.
//
//  Its current answer is worth knowing for reading the behaviour table: it is true for any
//  non-null reference, INCLUDING a disposed one, because disposal is not consulted. So a disposed
//  but non-null argument is walked normally here rather than rejected. That follows from
//  IsValidObject and is not a separate choice made in this file.
//
//  DECISION 8 - NO INPUT THROWS, INCLUDING WHEN REFLECTION ITSELF FAILS
//  --------------------------------------------------------------------------------------------
//  All four legacy entry points are declared `global function boolean` and every readable path
//  through them returns true or false. Bad input is answered, not rejected: an empty class name
//  or an empty parent name returns false at isancestorbyclass.srf:L12, and an invalid object
//  returns false at isancestorbyobject.srf:L12. Nothing raises. So no member here throws for any
//  input, and in particular none throws ArgumentException or ArgumentNullException for an empty
//  or null string. Guarding with string.IsNullOrEmpty rather than a bare `== ""` extends the
//  oracle's empty test to cover a null arriving from a nullable-oblivious caller, which is the
//  pragmatic reading: the alternative would be to let a null reach a comparison and answer false
//  by accident instead of by decision, and the answer is the same either way.
//
//  Reflection is the one place a throw could arrive from somewhere other than the arguments.
//  Assembly.GetTypes raises ReflectionTypeLoadException when an assembly is only partially
//  loadable, and the surrounding load family - a missing or unloadable dependency, a malformed
//  image, a type that cannot be prepared - can surface for the same underlying reason. Every one
//  of those is caught, and the assembly that raised is SKIPPED so the scan continues over the
//  rest. Two properties follow, both intended: nothing propagates out of this class, and a type
//  reachable only through an assembly that cannot be enumerated does not resolve, which yields
//  false. Returning false for what cannot be determined is the faithful posture for a predicate
//  whose oracle answers false for everything it cannot resolve.
//
//  The partially-available type list that ReflectionTypeLoadException carries is deliberately not
//  salvaged. Harvesting it would make resolution succeed sometimes and fail other times for the
//  same broken assembly depending on where in its metadata the failure fell, which is exactly the
//  kind of order-dependent answer DECISION 4 rejects.
//
//  DECISION 9 - NO CONSTANT AND NO UNDERSCORE IDENTIFIER IS DECLARED IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  A hard build constraint, not a preference. The refactor preserves the legacy SCREAMING_SNAKE
//  constant spellings verbatim, and the repository-root .editorconfig suppresses the naming
//  diagnostics that would reject them for a SHORT EXPLICIT LIST of files that genuinely carry
//  such identifiers. In this project that list is RetCode.cs and Enums.cs only. Ancestry.cs is
//  NOT on it, and Directory.Build.props sets TreatWarningsAsErrors, so a declared identifier
//  containing an underscore here is a BUILD ERROR rather than advice. Nothing below declares one,
//  and this file must never acquire a suppression of its own to make one possible - widening that
//  scoping is explicitly forbidden by both of those files.
//
//  Legacy class names still appear here in abundance, as they must, but only ever as STRING
//  VALUES in comments and documentation - "n_cst_dwsvc", "olecustomcontrol" - never as C#
//  identifiers. The distinction is what keeps this file clean under the analyzer.
//
//  THE COMPLETE BEHAVIOUR TABLE
//  --------------------------------------------------------------------------------------------
//  Read against a three-level hierarchy Base <- Middle <- Leaf. Derived from the bodies below,
//  not from intuition. The starred rows are the ones a "correction" would break, and each is
//  pinned by a test.
//
//      CALL                                                       ANSWER
//      IsAncestorByObject(new Leaf(), "Leaf")                      TRUE*   self-inclusive
//      IsAncestorByObject(new Leaf(), "Middle")                    true
//      IsAncestorByObject(new Leaf(), "Base")                      true
//      IsAncestorByObject(new Leaf(), "Object")                    TRUE*   chain root, DECISION 1
//      IsAncestorByObject(new Base(), "Leaf")                      false   the walk goes up only
//      IsAncestorByObject(new Base(), "leaf")                      false   ordinal, DECISION 2
//      IsAncestorByObject(null, "Leaf")                            false   IsValidObject rejects
//      IsAncestorByObject(new Leaf(), "")                          false   empty parent
//      IsAncestorByObject(disposedNonNull, "Leaf")                 TRUE*   DECISION 7
//      IsAncestorByObject(42, "ValueType")                         true    boxed, Int32 chain
//      IsAncestorByClass("Leaf", "Leaf")                           TRUE*   self-inclusive
//      IsAncestorByClass("Leaf", "Base")                           true
//      IsAncestorByClass("Leaf", "leaf")                           false   ordinal
//      IsAncestorByClass("", "Base")                               false   empty class
//      IsAncestorByClass(null, "Base")                             false   DECISION 8
//      IsAncestorByClass("NoSuchTypeAnywhere", "Base")             false   unresolvable
//      IsAncestorByClass("IDisposable", "Object")                  false   interface, DECISION 3
//      IsAncestor(anything, anything)                     same as the By... member it delegates to
//      IsAncestor(null, "Base")                                    false   binds to STRING, DEC 5
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules govern this file: the project's rules document contains exactly one line, "No
//  user rules provided". The binding constraints are therefore the refactor's own, and
//  enterprise-standard practice stands in place of the absent rules rather than the bar being
//  lowered. Per constraint:
//
//    C-A   Shared implementation consumed in process. Pure functions over reflection metadata:
//          no I/O, no filesystem access, no assembly loading, no logging, no state, no cache, and
//          no reference to PowerFramework.Contracts or to anything under services/. The class is
//          stateless and therefore trivially thread safe.
//    C-B   No behaviour improvement. The self-inclusive walk, the unqualified-name comparison,
//          the false-not-throw guards and all four entry points - including the three with zero
//          call sites - are reproduced as found. Nothing is narrowed to proper-ancestor, nothing
//          is upgraded to FullName, and no member is added beyond the legacy four (DECISION 6).
//    C-C   The legacy tree is the read-only oracle. All three .srf files were read in full and
//          none was modified; every member cites the ws_objects locator it reproduces.
//    C-K   Every technology-specific decision is documented at its point of reproduction: the
//          ClassDefinition and Ancestor substitution (1), the unqualified Type.Name comparison
//          with its namespace-collision consequence and the ordinal choice (2), base-types-only
//          and why IsAssignableFrom is unusable (3), the FindClassDefinition scope, tie-break,
//          limitation and no-cache posture (4), the inferred native semantics (5), the rejected
//          Type overload (6), the Predicates.IsValidObject dependency (7), and the exception-free
//          posture including reflection failure (8).
//    0.4.5.2  Object-kind mapping. Three *.srf global functions become static methods on a
//          role-named static class, so Ancestry.IsAncestor and siblings.
//    0.6.5    Native-substitution matrix. The ancestry primitives are classified SUBSTITUTE via
//          type-assignability checks, so pfw.dll is substituted rather than reverse engineered.
//          The substitute is a name walk rather than IsAssignableFrom itself, for the two reasons
//          DECISION 3 gives; the classification sanctions the substitution, not one spelling of it.
// ==============================================================================================

using System.Reflection;

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework type-ancestry predicates, ported from <c>isancestor.srf</c>,
/// <c>isancestorbyclass.srf</c> and <c>isancestorbyobject.srf</c> under
/// <c>ws_objects/pfw.common.pbl.src/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Four pure static predicates that answer "does this thing's inheritance chain contain a class
/// with this name". They exist to reproduce the legacy dispatch test, whose canonical shape is
/// <c>IsAncestor(target, "n_cst_dwsvc")</c> at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L603</c>.
/// </para>
/// <para>
/// THE WALK IS SELF-INCLUSIVE: A TYPE IS ITS OWN ANCESTOR. The oracle compares before it advances
/// [isancestorbyclass.srf:L16-L17], so <c>IsAncestorByClass("Foo", "Foo")</c> and
/// <c>IsAncestorByObject(new Foo(), "Foo")</c> are both <see langword="true"/>. This is deliberate
/// legacy behaviour and it is what makes the dispatch tests at the 30 real call sites work, since
/// an exact match must answer yes there just as a subclass must. Do not narrow it to
/// proper-ancestor-only.
/// </para>
/// <para>
/// Comparison is on the UNQUALIFIED <see cref="System.Type.Name"/>, ordinally, never on
/// <see cref="System.Type.FullName"/>. PowerBuilder has one flat global namespace and every legacy
/// call site passes a bare class name, so a qualified comparison would answer
/// <see langword="false"/> everywhere. The consequence of an unqualified comparison in a tree that
/// DOES have namespaces is that two same-named types in different namespaces both satisfy the
/// predicate against that name; that is faithful and is pinned by a test. See DECISION 2 in this
/// file's header.
/// </para>
/// <para>
/// The chain walked is <see cref="System.Type.BaseType"/> only. Interfaces are NOT walked and
/// <see cref="System.Type.IsAssignableFrom(System.Type)"/> is deliberately not used: the target
/// arrives only as a runtime string, and interface assignability would answer a wider question than
/// the oracle's single-inheritance chain can express. See DECISION 3.
/// </para>
/// <para>
/// Every member is total: none throws for any input, none validates its argument by raising, and
/// none has any side effect, so a caller needs no try/catch and no pre-check. Bad input answers
/// <see langword="false"/>, exactly as the oracle does [isancestorbyclass.srf:L12,
/// isancestorbyobject.srf:L12]. The class holds no state and is therefore trivially thread safe.
/// </para>
/// <para>
/// Read the complete behaviour table in this file's header before relying on an edge case. The
/// rows most often got wrong are the self-inclusive answers, the fact that <c>"Object"</c> matches
/// every class because the .NET chain terminates at <see cref="object"/>, and that an interface
/// named as the class argument does NOT reach <c>"Object"</c>.
/// </para>
/// </remarks>
public static class Ancestry
{
    // ------------------------------------------------------------------------------------------
    //  IsAncestor                       ws_objects/pfw.common.pbl.src/isancestor.srf (11 lines)
    //  --------------------------------------------------------------------------------------
    //  NATIVE, and the only one of the three with real call sites: 30 of them, every single one
    //  using the object-shaped prototype below. The string-shaped prototype at isancestor.srf:L8
    //  has none. isancestor.srf:L3 binds the whole function object to pfw.dll and the file has no
    //  body at all, so both overloads reproduce the readable siblings the prototypes mirror
    //  rather than a body that can be read. DECISION 5 explains why they delegate.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether the runtime class of <paramref name="value"/>, or any class it inherits
    /// from, is named <paramref name="parentClass"/>. [isancestor.srf:L7]
    /// </summary>
    /// <param name="value">
    /// The object whose inheritance chain is tested, or <see langword="null"/>. Corresponds to the
    /// legacy <c>readonly powerobject object</c> parameter.
    /// </param>
    /// <param name="parentClass">
    /// The unqualified class name to look for. Compared ordinally against
    /// <see cref="System.Type.Name"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/>'s runtime type or any of its base types
    /// is named <paramref name="parentClass"/>; otherwise <see langword="false"/>. Also
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/> or
    /// <paramref name="parentClass"/> is null or empty. Never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WALK IS SELF-INCLUSIVE, so an exact match answers <see langword="true"/>. This overload
    /// is the one all 30 legacy call sites use, and they rely on that: they are dispatch tests of
    /// the form "is this thing a <c>T</c>".
    /// </para>
    /// <para>
    /// INFERRED, NOT TRANSCRIBED. <c>isancestor.srf:L3</c> declares the function object
    /// <c>native "pfw.dll"</c> and the file contains no body; no C++ source for that binary exists
    /// anywhere in the repository. Its behaviour is therefore inferred from
    /// <see cref="IsAncestorByObject(object?, string)"/>, whose legacy prototype
    /// [isancestorbyobject.srf:L7] mirrors <c>isancestor.srf:L7</c> exactly and whose body IS
    /// readable. This method delegates to it rather than carrying a second copy of the walk,
    /// so the inferred semantics and the specification they were inferred from cannot diverge.
    /// See DECISION 5.
    /// </para>
    /// <para>
    /// Note the overload resolution: <c>IsAncestor(null, "X")</c> binds to
    /// <see cref="IsAncestor(string?, string)"/>, not to this member, because <c>string</c> is the
    /// more specific parameter type. Both answer <see langword="false"/>, so the result is
    /// unaffected.
    /// </para>
    /// </remarks>
    public static bool IsAncestor(object? value, string parentClass)
    {
        // isancestor.srf:L7 - `global function boolean isancestor (readonly powerobject object,
        // readonly string parentcls)`. No body exists to transcribe; the mirroring managed sibling
        // is the specification, so the call below IS the port rather than a shortcut to it.
        return IsAncestorByObject(value, parentClass);
    }

    /// <summary>
    /// Reports whether the class named <paramref name="className"/>, or any class it inherits from,
    /// is named <paramref name="parentClass"/>. [isancestor.srf:L8]
    /// </summary>
    /// <param name="className">
    /// The unqualified name of the class whose inheritance chain is tested, or
    /// <see langword="null"/>. Corresponds to the legacy <c>readonly string cls</c> parameter.
    /// </param>
    /// <param name="parentClass">
    /// The unqualified class name to look for. Compared ordinally against
    /// <see cref="System.Type.Name"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the resolved class or any of its base types is named
    /// <paramref name="parentClass"/>; otherwise <see langword="false"/>. Also
    /// <see langword="false"/> when either argument is null or empty, or when
    /// <paramref name="className"/> resolves to no loaded type. Never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WALK IS SELF-INCLUSIVE, so <c>IsAncestor("Foo", "Foo")</c> answers
    /// <see langword="true"/>.
    /// </para>
    /// <para>
    /// INFERRED, NOT TRANSCRIBED, for the same reason as the object-shaped overload:
    /// <c>isancestor.srf:L3</c> is a <c>native "pfw.dll"</c> binding with no body. Behaviour is
    /// inferred from <see cref="IsAncestorByClass(string?, string)"/>, whose legacy prototype
    /// [isancestorbyclass.srf:L7] mirrors <c>isancestor.srf:L8</c> exactly, and this method
    /// delegates to it so the two cannot diverge. See DECISION 5.
    /// </para>
    /// <para>
    /// This prototype has ZERO call sites anywhere in <c>ws_objects</c> - measured, not assumed -
    /// so nothing in the legacy exercises it. It is ported regardless, because it is part of the
    /// declared surface. Resolving a bare name is also the weakest of the four paths: it can only
    /// find types in ALREADY-LOADED assemblies, and its tie-break among same-named types is a rule
    /// this port defines rather than inherits. Both are documented at
    /// <see cref="IsAncestorByClass(string?, string)"/> and in DECISION 4.
    /// </para>
    /// </remarks>
    public static bool IsAncestor(string? className, string parentClass)
    {
        // isancestor.srf:L8 - `global function boolean isancestor (readonly string cls, readonly
        // string parentcls)`. Native, no body; delegation to the mirroring managed sibling is the
        // port. See DECISION 5.
        return IsAncestorByClass(className, parentClass);
    }

    // ------------------------------------------------------------------------------------------
    //  IsAncestorByObject     ws_objects/pfw.common.pbl.src/isancestorbyobject.srf (23 lines)
    //  --------------------------------------------------------------------------------------
    //  Pure PowerScript and fully readable, which is why it - not the native function - is the
    //  specification for object-shaped ancestry. Zero call sites in the legacy; it is the managed
    //  fallback, and it is ported in full regardless (constraint C-B).
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether the runtime class of <paramref name="value"/>, or any class it inherits
    /// from, is named <paramref name="parentClass"/>. [isancestorbyobject.srf:L10-L21]
    /// </summary>
    /// <param name="value">
    /// The object whose inheritance chain is tested, or <see langword="null"/>. Validity is decided
    /// by <see cref="Predicates.IsValidObject(object?)"/>, not by an inline null test.
    /// </param>
    /// <param name="parentClass">
    /// The unqualified class name to look for. Compared ordinally against
    /// <see cref="System.Type.Name"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/>'s runtime type or any of its base types
    /// is named <paramref name="parentClass"/>; otherwise <see langword="false"/>. Never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WALK IS SELF-INCLUSIVE. The oracle seeds the loop with the object's own class
    /// [isancestorbyobject.srf:L14] and compares before advancing [L16-L17], so
    /// <c>IsAncestorByObject(new Foo(), "Foo")</c> is <see langword="true"/>.
    /// </para>
    /// <para>
    /// THE GUARD DELEGATES TO <see cref="Predicates.IsValidObject(object?)"/>, reproducing the
    /// oracle's own call [isancestorbyobject.srf:L12] rather than inlining an equivalent null test.
    /// That function carries a documented substitution for PowerBuilder's <c>IsValid</c> and
    /// deliberately does not consult .NET disposal, so a disposed but non-null argument is walked
    /// normally here. That behaviour is inherited from it, not chosen here; see DECISION 7.
    /// </para>
    /// <para>
    /// SUBSTITUTIONS. <c>object.ClassDefinition</c> becomes <see cref="object.GetType()"/> and
    /// <c>ClassDefinition.Ancestor</c> becomes <see cref="System.Type.BaseType"/>. Because the .NET
    /// chain terminates at <see cref="object"/>, the name <c>"Object"</c> is in every chain and
    /// matches everything; the legacy chain terminated at PowerBuilder's own root instead, so the
    /// shape is preserved while the root's spelling is a platform fact. A boxed value type is
    /// walked as its own type, so <c>42</c> matches <c>"Int32"</c>, <c>"ValueType"</c> and
    /// <c>"Object"</c>. See DECISION 1.
    /// </para>
    /// </remarks>
    public static bool IsAncestorByObject(object? value, string parentClass)
    {
        // isancestorbyobject.srf:L12 - `if Not IsValidObject(object) or parentCls = "" then return
        // false`.
        //
        // Predicates.IsValidObject is CALLED rather than inlined, because the oracle calls it and
        // because it owns the IsValid substitution and its disposal decision (DECISION 7). An
        // inline `value is null` would fork that decision and let the two drift.
        //
        // The empty test is widened from the oracle's `= ""` to IsNullOrEmpty so that a null
        // arriving from a nullable-oblivious caller answers false by decision rather than by
        // accident. The answer is identical either way; DECISION 8 records the reasoning.
        if (!Predicates.IsValidObject(value) || string.IsNullOrEmpty(parentClass))
        {
            return false;
        }

        // isancestorbyobject.srf:L14 - `clsDef = object.ClassDefinition`. The seed is the object's
        // OWN class, which is what makes the walk self-inclusive. The null-forgiving operator is
        // sound here because IsValidObject above has already established that value is not null.
        //
        // isancestorbyobject.srf:L15-L20 - the walk itself, shared with IsAncestorByClass.
        return ChainContainsName(value!.GetType(), parentClass);
    }

    // ------------------------------------------------------------------------------------------
    //  IsAncestorByClass       ws_objects/pfw.common.pbl.src/isancestorbyclass.srf (23 lines)
    //  --------------------------------------------------------------------------------------
    //  Pure PowerScript and fully readable, and the model for the whole file: its L12-L20 is the
    //  guard, the seed, the walk and the fallthrough in nine lines. Zero call sites in the legacy;
    //  ported in full regardless (constraint C-B).
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether the class named <paramref name="className"/>, or any class it inherits from,
    /// is named <paramref name="parentClass"/>. [isancestorbyclass.srf:L10-L21]
    /// </summary>
    /// <param name="className">
    /// The unqualified name of the class whose inheritance chain is tested, or
    /// <see langword="null"/>. Resolved against the already-loaded assemblies of the current
    /// application domain.
    /// </param>
    /// <param name="parentClass">
    /// The unqualified class name to look for. Compared ordinally against
    /// <see cref="System.Type.Name"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the resolved class or any of its base types is named
    /// <paramref name="parentClass"/>; otherwise <see langword="false"/>, including when either
    /// argument is null or empty and when <paramref name="className"/> resolves to no loaded type.
    /// Never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WALK IS SELF-INCLUSIVE. The oracle seeds the loop with the resolved class itself
    /// [isancestorbyclass.srf:L14] and compares before advancing [L16-L17], so
    /// <c>IsAncestorByClass("Foo", "Foo")</c> is <see langword="true"/>.
    /// </para>
    /// <para>
    /// RESOLUTION SCOPE, AND ITS LIMIT. <c>FindClassDefinition(cls)</c> [isancestorbyclass.srf:L14]
    /// searches PowerBuilder's one flat global namespace, where a name is unique. .NET has no such
    /// namespace, so this port searches the ALREADY-LOADED assemblies of the current application
    /// domain and nothing else: it does not load an assembly, does not touch the filesystem and
    /// does not use <c>Assembly.LoadFrom</c>. A type in an assembly not yet loaded therefore WILL
    /// NOT be found, where the legacy would have found it. That is an unavoidable consequence of
    /// replacing a flat namespace with lazily-loaded assemblies, and the failure mode is a
    /// <see langword="false"/> rather than a throw.
    /// </para>
    /// <para>
    /// TIE-BREAK. Because namespaces are new to this tree, two loaded types can share an
    /// unqualified name where the legacy could hold only one, so this is a case with no legacy
    /// answer. Exactly one type is resolved and only that one is walked, mirroring the oracle's
    /// single <c>ClassDefinition</c> handle; the winner is the match whose
    /// <see cref="System.Type.FullName"/> is ordinally smallest. Stopping at the first match found
    /// is deliberately NOT done, because neither assembly nor type enumeration order is guaranteed
    /// by the runtime and the answer would then depend on load order. See DECISION 4.
    /// </para>
    /// <para>
    /// An interface named here has a chain of exactly one entry, because
    /// <c>typeof(ISomething).BaseType</c> is <see langword="null"/>. So
    /// <c>IsAncestorByClass("IDisposable", "IDisposable")</c> is <see langword="true"/> while
    /// <c>IsAncestorByClass("IDisposable", "Object")</c> is <see langword="false"/>. For a
    /// constructed generic type <see cref="System.Type.Name"/> carries the runtime's arity suffix,
    /// so the name to pass is of the form <c>List`1</c>; the legacy had no generics and so no
    /// behaviour to compare against.
    /// </para>
    /// </remarks>
    public static bool IsAncestorByClass(string? className, string parentClass)
    {
        // isancestorbyclass.srf:L12 - `if cls = "" or parentCls = "" then return false`.
        //
        // Both arguments are tested, in the oracle's order. IsNullOrEmpty rather than `== ""` for
        // the reason given in DECISION 8: a null must answer false by decision, not by accident.
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(parentClass))
        {
            return false;
        }

        // isancestorbyclass.srf:L14 - `clsDef = FindClassDefinition(cls)`. Substituted by a lookup
        // over the already-loaded assemblies; see DECISION 4 for the scope, the tie-break and the
        // limitation. A failed lookup yields null, which the walk below reports as false - matching
        // the oracle, whose L15 `do while IsValid(clsDef)` never enters when the lookup failed.
        Type? resolvedClass = ResolveLoadedTypeByName(className);

        // isancestorbyclass.srf:L15-L20 - the walk itself, shared with IsAncestorByObject.
        return ChainContainsName(resolvedClass, parentClass);
    }

    // ==========================================================================================
    //  PRIVATE - the shared walk and the name lookup
    //  ----------------------------------------------------------------------------------------
    //  Both public paths funnel through ChainContainsName, so the self-inclusive seed, the
    //  base-types-only chain and the ordinal comparison are expressed EXACTLY ONCE. That is
    //  deliberate: the oracle's two readable bodies are byte-identical from L15 to L20, so a
    //  second copy here would be a place for them to drift apart in a port whose whole purpose is
    //  fidelity. The two differ only in their SEED and their GUARD, which is precisely what the
    //  two public members above carry and the only thing they carry.
    //
    //  Neither helper is part of the public surface, and neither may become so: they are
    //  implementation detail of a four-member contract taken from four legacy prototypes, and
    //  promoting one would widen that surface past the oracle.
    // ==========================================================================================

    /// <summary>
    /// Walks the base-type chain from <paramref name="startType"/> upwards, reporting whether any
    /// link is named <paramref name="parentClass"/>. The single shared reproduction of
    /// <c>isancestorbyclass.srf:L15-L20</c> and <c>isancestorbyobject.srf:L15-L20</c>.
    /// </summary>
    /// <param name="startType">
    /// The type the walk is seeded with - the class ITSELF, which is what makes the walk
    /// self-inclusive. <see langword="null"/> yields <see langword="false"/>, reproducing the
    /// oracle's loop never being entered after a failed lookup.
    /// </param>
    /// <param name="parentClass">
    /// The unqualified class name to look for. Guaranteed non-empty by both callers.
    /// </param>
    /// <returns>
    /// <see langword="true"/> on the first link whose <see cref="System.Type.Name"/> equals
    /// <paramref name="parentClass"/> ordinally; otherwise <see langword="false"/>.
    /// </returns>
    private static bool ChainContainsName(Type? startType, string parentClass)
    {
        // isancestorbyclass.srf:L15 / isancestorbyobject.srf:L15 - `do while IsValid(clsDef)`.
        //
        // `IsValid(clsDef)` becomes a null test: ClassDefinition.Ancestor yields an invalid handle
        // at the root of the chain and Type.BaseType yields null, so both loops terminate for the
        // same structural reason (DECISION 1). Seeding `current` from the parameter rather than
        // advancing first is what preserves the SELF-INCLUSIVE semantics - the seed is tested
        // before any base type is, so a type is its own ancestor.
        Type? current = startType;

        while (current is not null)
        {
            // isancestorbyclass.srf:L16 / isancestorbyobject.srf:L16 -
            // `if clsDef.name=parentCls then return true`.
            //
            // `clsDef.name` is an UNQUALIFIED PowerBuilder class name, because PowerBuilder has one
            // flat global namespace, so the counterpart is Type.Name and NOT Type.FullName or
            // Type.AssemblyQualifiedName - every legacy call site passes a bare class name and a
            // qualified comparison would answer false at all 30 of them (DECISION 2).
            //
            // StringComparison.Ordinal because PowerScript `=` on strings is a byte comparison. A
            // culture-sensitive comparison would also make the answer depend on the ambient
            // culture, which characterization recordings cannot tolerate.
            if (string.Equals(current.Name, parentClass, StringComparison.Ordinal))
            {
                return true;
            }

            // isancestorbyclass.srf:L17 / isancestorbyobject.srf:L17 - `clsDef = clsDef.Ancestor`.
            //
            // BaseType only. Implemented interfaces are deliberately NOT traversed: the legacy
            // Ancestor chain is single inheritance and PowerBuilder has no interfaces for it to
            // reach, so walking them would answer a wider question than the oracle can express
            // (DECISION 3). This is also why Type.IsAssignableFrom is not used here.
            current = current.BaseType;
        }

        // isancestorbyclass.srf:L20 / isancestorbyobject.srf:L20 - `return false`.
        return false;
    }

    /// <summary>
    /// Resolves an unqualified class name to a single loaded <see cref="Type"/>. The substitution
    /// for <c>FindClassDefinition(cls)</c> at <c>isancestorbyclass.srf:L14</c>.
    /// </summary>
    /// <param name="className">
    /// The unqualified class name to resolve. Guaranteed non-empty by the caller.
    /// </param>
    /// <returns>
    /// The matching type whose <see cref="System.Type.FullName"/> is ordinally smallest, or
    /// <see langword="null"/> when no already-loaded assembly declares a type with that name.
    /// </returns>
    /// <remarks>
    /// Searches only the already-loaded assemblies of the current application domain: it loads
    /// nothing, touches no file and never throws. See DECISION 4 for the scope, the deterministic
    /// tie-break, the honest limitation and why no result is cached.
    /// </remarks>
    private static Type? ResolveLoadedTypeByName(string className)
    {
        Type? resolved = null;
        string? resolvedFullName = null;

        // DECISION 4: the scope is what is ALREADY LOADED. GetAssemblies reports the current set
        // and does not load anything. Assembly.LoadFrom, Assembly.LoadFile, Assembly.Load and any
        // filesystem probing are deliberately absent and must stay absent - this library is pure
        // behaviour with no I/O (constraint C-A), and its project file states that as a property
        // of the whole assembly.
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            Assembly assembly = assemblies[assemblyIndex];

            // Dynamic assemblies are skipped: they are no part of any flat-namespace analogue, and
            // enumerating their types is not universally supported. Skipping them here also removes
            // the one routine source of NotSupportedException from the loop below.
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception exception) when (
                exception is ReflectionTypeLoadException
                    or FileNotFoundException
                    or FileLoadException
                    or BadImageFormatException
                    or TypeLoadException
                    or NotSupportedException)
            {
                // DECISION 8. A partially loadable assembly is SKIPPED, so the scan continues over
                // the rest and nothing propagates out of this class - the oracle never throws, and
                // neither may this. A type reachable only through an assembly that cannot be
                // enumerated therefore fails to resolve, which the caller reports as false;
                // answering false for what cannot be determined is the faithful posture.
                //
                // The partially-available list that ReflectionTypeLoadException carries in its
                // Types property is deliberately NOT salvaged. Harvesting it would make resolution
                // succeed or fail for the same broken assembly depending on where in its metadata
                // the failure fell, which is exactly the order-dependent answer DECISION 4 rejects.
                continue;
            }

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type candidate = types[typeIndex];

                // Ordinal, and against the UNQUALIFIED name, for the same two reasons the walk
                // uses them: PowerScript `=` is a byte comparison, and `ClassDefinition.name` is a
                // bare class name (DECISION 2).
                if (!string.Equals(candidate.Name, className, StringComparison.Ordinal))
                {
                    continue;
                }

                // DECISION 4's deterministic tie-break. FullName is null for a few exotic type
                // shapes, so Name stands in for it there; that only affects which of two same-named
                // candidates wins, never whether a match was found.
                string candidateFullName = candidate.FullName ?? candidate.Name;

                // Keep the ordinally smallest FullName rather than the first match encountered.
                // The scan therefore runs to completion instead of exiting early, ON PURPOSE:
                // neither the order of GetAssemblies nor the order of GetTypes is guaranteed by the
                // runtime, so "first" would make the answer depend on load order and the same input
                // could answer differently across two runs of the same program. No performance
                // claim is made or implied by that trade in either direction; the repository
                // publishes no latency or throughput target, so correctness and reproducibility are
                // the only criteria available to decide it.
                if (resolved is null || string.CompareOrdinal(candidateFullName, resolvedFullName) < 0)
                {
                    resolved = candidate;
                    resolvedFullName = candidateFullName;
                }
            }
        }

        // A failed lookup yields null, which ChainContainsName reports as false. That matches the
        // oracle, whose `do while IsValid(clsDef)` at isancestorbyclass.srf:L15 is never entered
        // when FindClassDefinition found nothing, so control falls through to L20's `return false`.
        return resolved;
    }
}
