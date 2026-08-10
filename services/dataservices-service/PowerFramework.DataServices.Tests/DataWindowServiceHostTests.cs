// =====================================================================================================
//  DataWindowServiceHostTests.cs
//  =====================================================================================================
//  UNIT UNDER TEST
//      services/dataservices-service/PowerFramework.DataServices/Domain/DataWindowServiceHost.cs
//
//      That one file declares six public types, and this suite is the ONLY coverage of the host
//      contract's own behaviour (constraint C-H). Every sibling suite in this folder consumes the
//      contract in order to test something else - the event chain, the expression engine, the four
//      headless service models - so if this file did not exist the contract itself would be exercised
//      only incidentally, and the structural invariant below would be asserted nowhere at all.
//
//  =====================================================================================================
//  WHY THIS SUITE IS THE AUDIT POINT FOR CONSTRAINT C-D
//  =====================================================================================================
//  `se_cst_dw` STRUCTURALLY INHERITS A DEFERRED DESIGNSYSTEM TYPE.
//  ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru declares, at :L4 in its forward block and
//  again at :L10 in its type block, `global type se_cst_dw from se_cst_datawindow`. Its parent
//  `se_cst_datawindow` lives in `pfw.ui.controls.ext`, a DEFERRED DesignSystem library, and derives in
//  turn from a further DesignSystem ancestor. That is an INHERITANCE EDGE and not a call, so no amount
//  of refactoring at a call site removes it.
//
//  AAP 0.2.1.3 CORRECTION 3 IS THE RESOLUTION, AND THIS SUITE IS ITS AUDIT.
//  The correction requires DataServices to declare its own abstract host contract carrying ONLY the
//  members `se_cst_dw` and `n_cst_dwsvc` actually consume, to implement against that contract, and to
//  record `se_cst_datawindow` as REFERENCE-only with nothing ported from it. Constraint C-D forbids
//  implementing a deferred service even partially and even to stub it out. Prose cannot enforce either
//  of those; a test can, so the Phase 1 region below turns them into executable assertions.
//
//  A FAILURE IN THE PHASE 1 REGION MEANS A DEFERRED-DESIGNSYSTEM CONCERN HAS LEAKED ACROSS THE
//  BOUNDARY. It is not a test that needs relaxing. The correct response is to remove the offending
//  member from the contract and surface its DATA over the published contract instead, naming the
//  rendering half as a reserved `/v1/design/**` Gateway extension point (AAP 0.4.4) - which is exactly
//  what AAP 0.2.1.3 Correction 4 already did for the presentational halves of ColumnSort, ContextMenu
//  and DropDownSearch. AAP 0.8.1 records the governing judgement: where the choice is between a
//  partial implementation and a documented gap, THE DOCUMENTED GAP WINS.
//
//  =====================================================================================================
//  BEHAVIOURAL ORACLE (constraint C-C: the legacy tree is READ ONLY and is never an edit target)
//  =====================================================================================================
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru    (864 lines)
//          :L9-L10     the `oninit` and `onenable` event declarations
//          :L18-L24    the STYLE_* presentation-style catalogue, WITH NO CONSTANT FOR 6
//          :L26-L32    the COL_TYPE_* column-type catalogue, contiguous 0 through 6
//          :L36-L38    `privatewrite #DataWindow`, `privatewrite #Eventful`, `protectedwrite #Enabled`
//          :L85-L87    `oninit`, which binds the host and then LIFTS the broker off it
//          :L89-L95    `of_setenabled`, the idempotent early-out and the veto-to-FAILED mapping
//          :L97        `_of_getdwobject(readonly long colnum)`, pure `"#" + String(colNum)` delegation
//          :L100-L125  `_of_getdwobject(readonly string dwoname)`, the IsValidObject guard at :L119 and
//                      the `Object.__Get_Attribute` lookup at :L122
//          :L486-L518  `_of_convertcoltype`, the `Left(colType,5)` prefix table
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru      (616 lines)
//          :L4, :L10   the inheritance edge this contract exists to cut
//          :L115-L400  the eleven semantic ancestry events, each at its own call site
//          :L120-L122  `ondwnrbuttonup`, BROKER ONLY - there is no semantic RButtonUp
//          :L343-L344  `rtCode = Event ItemError(...)` then `if IsNull(rtCode) then rtCode = 0`
//          :L395-L397  `ondwnlbuttonup`, BROKER ONLY - there is no semantic LButtonUp
//      ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru  (91 lines)
//          READ AS REFERENCE ONLY. Not one line of it is ported, and nothing in this suite doubles,
//          names or types any of its members. Its entire content is theming: three theme events at
//          :L9-L11, a `ThemeManager().#Style` switch at :L15-L54, an `ongetcolor` override at
//          :L64-L72, `Win32.RedrawWindow(#Handle, ...)` at :L74-L80, and control registration and
//          unregistration at :L82-L90.
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                    (37 lines)
//          :L8-L13     THE PRIMARY FIXTURE. The only updatable DataWindow in the whole repository, and
//                      the source of the six real `ColType` strings the normalisation theory is driven
//                      from: `number`, `char(100)`, `number`, `char(200)`, `decimal(2)`, `date`.
//      ws_objects/pfw.shared.pbl.src/retcode.sru
//          :L39-L45    OK/SUCCESS/ALLOW = 0, PREVENT = 1, FAILED = -1, CANCELED/CANCELLED = -2
//      ws_objects/pfw.shared.pbl.src/isvalidobject.srf
//          :L11-L12    `if IsNull(object) then return false` / `return IsValid(object)`
//
//  NOTHING HERE READS THE LEGACY TREE. Every `ws_objects/**` path above appears only in a comment.
//  Oracle values are TRANSCRIBED as C# literals, each carrying the locator it came from (constraint
//  C-K), and no `.sru` or `.srd` file is opened at build time or at run time - the root .dockerignore
//  excludes ws_objects/ from the build context, so a read would fail in a container and in CI.
//
//  =====================================================================================================
//  ASSERT THE LEGACY VALUE EVEN WHERE IT LOOKS WRONG (constraint C-B, goal G2)
//  =====================================================================================================
//  Four preserved quirks are pinned below precisely because a well-meaning reader would "fix" them:
//
//      1. A VETOED `SetEnabled` RETURNS `FAILED` (-1), NOT `PREVENT` (1) [n_cst_dwsvc.sru:L90]. This
//         one matters twice over, because the two codes sit on opposite sides of the tri-state
//         predicate algebra: `Predicates.IsSucceeded(PREVENT)` is TRUE while `IsSucceeded(FAILED)` is
//         false, so confusing them inverts the caller's success test. The companion assertion runs the
//         real vetoed result through both predicates so the interaction is visible rather than implied.
//      2. ONLY THE EXACT SIGNAL `1` VETOES [n_cst_dwsvc.sru:L90 tests `= 1`]. A hook returning 2, or
//         even returning `RetCode.FAILED` itself, ALLOWS the change.
//      3. `STYLE_RICHTEXT` IS 7 AND NOTHING IS 6 [n_cst_dwsvc.sru:L18-L24]. Closing the gap would
//         silently remap richtext, so the gap is asserted directly AND through its observable
//         consequence: a `DataWindow.Processing` answer of "6" resolves to STYLE_DEFAULT.
//      4. `ItemError`'S DEFAULT IS `null`, NOT `0` [se_cst_dw.sru:L343-L344]. The oracle coerces null
//         to zero at the CALL SITE, so a contract that defaulted to zero would make that ported line
//         dead code and silently delete a branch.
//
//  =====================================================================================================
//  HOUSE RULES THIS FILE OBEYS
//  =====================================================================================================
//  PLAIN XUNIT `Assert` ONLY. There is no assertion library and no mocking library anywhere in the
//      dependency inventory (AAP 0.5.1), and AAP 0.5.3 forbids adding an "obvious" package; a sixth
//      package would additionally fail restore outright, because central package management carries no
//      PackageVersion entry for it. Every double used here is hand written and already exists in
//      FakeDataWindowHost.cs and TestDoubles.cs.
//  TABLE-DRIVEN THEORIES WITH MEMBER DATA, per AAP 0.6.7's test-shape requirement, rather than a wall
//      of separate facts. Each matrix carries its oracle locator in the initializer.
//  NO SCREAMING_SNAKE IDENTIFIER IS DECLARED HERE. The root .editorconfig scopes its CA1707 and
//      IDE1006 suppressions to the individually named non-test files on its BAND 3 roster - the single
//      source of truth for that list - and NO TEST FILE IS ON IT. Under TreatWarningsAsErrors a naming
//      diagnostic here would be an error rather than a warning. This file freely REFERENCES the
//      preserved constants (`RetCode.FAILED`, `DataWindowServiceBase.COL_TYPE_STRING`) and declares
//      none of its own.
//  NO DEFERRED-SERVICE TYPE IS REFERENCED, and that is itself asserted rather than merely intended -
//      see NoSurfaceTypeComesFromADeferredService below.
//  NO STORAGE, NO SQL, NO NETWORK, NO SECRET. Everything here is in-memory reflection over the
//      contract plus the hand-written host double. Persistence is the only service in the system that
//      holds a storage provider (constraint C-E), and nothing below is security sensitive
//      (constraint C-F).
//  NO PERFORMANCE CLAIM (AAP 0.8.5). The repository publishes no SLA, no latency budget, no throughput
//      target and no availability commitment, so no assertion below is described as fast or optimised.
// =====================================================================================================

// The BCL namespaces are stated EXPLICITLY even though the repository-root Directory.Build.props enables
// ImplicitUsings and therefore already supplies System, System.Collections.Generic and System.Linq. That
// is the convention the immediate neighbours follow - FakeDataWindowHost.cs and TestDoubles.cs both
// restate their BCL usings - and it keeps this file's dependencies legible without a reader having to
// know what the props file turns on. Every namespace below contributes symbols this file actually uses:
// System (StringComparison, StringComparer, Nullable, Enum, Type, the exception types),
// System.Collections.Generic (List, IEnumerable, IReadOnlyList, KeyNotFoundException),
// System.Globalization (CultureInfo.InvariantCulture in the assertion messages), System.Linq (the query
// operators every structural invariant is built from) and System.Reflection (the whole surface model).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

// The internal import surface is deliberately narrow: the contract under test, and the shared kernel
// for `RetCode` and `Predicates`. `PowerFramework.Shared.Eventful` is NOT imported even though the
// contract's `Eventful` member is typed as its `EventBroker` - nothing here needs to NAME that type,
// because every assertion about the broker compares host and service references for identity, which is
// the actual claim being made [n_cst_dwsvc.sru:L86 lifts the broker off the host rather than creating
// one]. The test doubles need no import at all: FakeDataWindowHost.cs and TestDoubles.cs share this
// file's namespace.
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;

using Xunit;

// The published buffer enum is ALIASED rather than imported wholesale, and the alias is mandatory
// rather than cosmetic. `PowerFramework.Contracts.Common.V1` also contains a generated wrapper message
// class named RetCode - common.v1.proto nests each legacy constant set as an enum named Value inside a
// thin wrapper message - so a plain namespace import would put a second `RetCode` in scope alongside
// PowerFramework.Shared.Kernel.RetCode and make every mention of it ambiguous (CS0104). Under
// TreatWarningsAsErrors that is a build failure. Aliasing exactly the one type needed keeps the wrapper
// message out of scope entirely, which is the identical decision the contract file and
// FakeDataWindowHost.cs both took.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterizes <c>Domain/DataWindowServiceHost.cs</c>: the C-D structural invariant, the eleven
/// semantic DataWindow events, the <c>SetEnabled</c> / <c>OnEnable</c> enablement protocol, the two
/// value-locked constant catalogues, and DataWindow-object resolution.
/// </summary>
public sealed class DataWindowServiceHostTests
{
    // ==============================================================================================
    //  THE CONTRACT SURFACE, MODELLED ONCE SO EVERY STRUCTURAL ASSERTION SHARES ONE DEFINITION
    // ==============================================================================================

    /// <summary>
    /// Every public type <c>Domain/DataWindowServiceHost.cs</c> declares. This list IS the contract
    /// surface, so a seventh type added to that file without being added here would escape the
    /// structural invariants below - which is why the count is asserted separately by
    /// <see cref="ContractFileDeclaresExactlySixPublicTypes"/>.
    /// </summary>
    private static readonly Type[] ContractTypes =
    [
        typeof(IDataWindowValueBuffer),
        typeof(IDataWindowObject),
        typeof(IDataWindowChild),
        typeof(DataWindowServiceHost),
        typeof(DataWindowServiceBase),
        typeof(DataWindowObjectExtensions),
    ];

    /// <summary>
    /// Declared members only, at every accessibility, so the filter to "externally reachable" is
    /// applied deliberately below rather than being delegated to a binding flag. Protected members
    /// ARE part of the contract, because an implementer derives from it.
    /// </summary>
    private const BindingFlags DeclaredAtAnyAccessibility =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// One externally reachable member of the contract surface, reduced to the text a structural
    /// invariant scans: its own name, and the spelling of every type it mentions.
    /// </summary>
    /// <param name="DeclaringType">The contract type that declares it, for the failure message.</param>
    /// <param name="Name">The member name.</param>
    /// <param name="TypeSpellings">
    /// Fully qualified spellings of the member's return type, parameter types, property type or field
    /// type, with array, by-reference and generic shapes unwrapped so a token hiding inside a generic
    /// argument is still visible.
    /// </param>
    private sealed record SurfaceMember(
        string DeclaringType,
        string Name,
        IReadOnlyList<string> TypeSpellings)
    {
        /// <summary>The member's location, for a readable assertion failure.</summary>
        public string Locator => DeclaringType + "." + Name;

        /// <summary>Everything a structural invariant scans for a forbidden token.</summary>
        public IEnumerable<string> ScannableText => TypeSpellings.Prepend(Name);
    }

    /// <summary>
    /// Enumerates every externally reachable member of every contract type.
    /// </summary>
    /// <remarks>
    /// PUBLIC AND PROTECTED, NOT PUBLIC ALONE. A protected member is reachable by anything that
    /// implements the contract, so a presentational member hidden behind <c>protected</c> would breach
    /// constraint C-D exactly as loudly as a public one. Private and internal members are excluded
    /// because they are implementation detail that no implementer can see.
    ///
    /// COMPILER-GENERATED ACCESSORS ARE SKIPPED. A property contributes its own entry, so admitting
    /// <c>get_Eventful</c> as well would double-count without adding a single new type spelling.
    /// Constructors are skipped for the same reason: they mention no type the members do not.
    /// </remarks>
    private static IReadOnlyList<SurfaceMember> Surface()
    {
        List<SurfaceMember> surface = [];

        foreach (Type type in ContractTypes)
        {
            foreach (MemberInfo member in type.GetMembers(DeclaredAtAnyAccessibility))
            {
                SurfaceMember? entry = Reduce(type, member);
                if (entry is not null)
                {
                    surface.Add(entry);
                }
            }
        }

        return surface;
    }

    /// <summary>
    /// Reduces one <see cref="MemberInfo"/> to a <see cref="SurfaceMember"/>, or to
    /// <see langword="null"/> when it is not part of the externally reachable surface.
    /// </summary>
    private static SurfaceMember? Reduce(Type declaringType, MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method when !method.IsSpecialName && IsReachable(method):
                return new SurfaceMember(
                    declaringType.Name,
                    method.Name,
                    [
                        Spell(method.ReturnType),
                        .. method.GetParameters().Select(parameter => Spell(parameter.ParameterType)),
                    ]);

            case PropertyInfo property when IsReachable(property.GetMethod)
                || IsReachable(property.SetMethod):
                return new SurfaceMember(
                    declaringType.Name,
                    property.Name,
                    [
                        Spell(property.PropertyType),
                        .. property.GetIndexParameters()
                            .Select(parameter => Spell(parameter.ParameterType)),
                    ]);

            case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                return new SurfaceMember(declaringType.Name, field.Name, [Spell(field.FieldType)]);

            case EventInfo declaredEvent:
                return new SurfaceMember(
                    declaringType.Name,
                    declaredEvent.Name,
                    [Spell(declaredEvent.EventHandlerType ?? typeof(void))]);

            case Type nested when nested.IsNestedPublic || nested.IsNestedFamily:
                return new SurfaceMember(declaringType.Name, nested.Name, [Spell(nested)]);

            default:
                return null;
        }
    }

    /// <summary>
    /// Whether a method is reachable from outside the assembly - either public, or protected in one of
    /// its two forms.
    /// </summary>
    private static bool IsReachable(MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    /// <summary>
    /// Spells a type fully, unwrapping the array, by-reference, pointer and generic shapes so a
    /// forbidden token cannot hide inside one of them.
    /// </summary>
    /// <remarks>
    /// THE NAMESPACE IS DELIBERATELY INCLUDED. A member typed as a DesignSystem type would most likely
    /// betray itself through its namespace rather than its bare name, so the scan would be materially
    /// weaker without it.
    /// </remarks>
    private static string Spell(Type type)
    {
        if (type.IsByRef || type.IsPointer)
        {
            return Spell(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return Spell(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            string definition = type.GetGenericTypeDefinition().FullName
                ?? type.GetGenericTypeDefinition().Name;
            int arity = definition.IndexOf('`', StringComparison.Ordinal);
            if (arity > 0)
            {
                definition = definition[..arity];
            }

            return definition
                + "<"
                + string.Join(",", type.GetGenericArguments().Select(Spell))
                + ">";
        }

        return type.FullName ?? type.Name;
    }

    // ==============================================================================================
    //  PHASE 1 - THE C-D STRUCTURAL INVARIANT
    //  --------------------------------------------------------------------------------------------
    //  THIS REGION IS THE AUDIT POINT FOR AAP 0.2.1.3 CORRECTION 3 AND FOR CONSTRAINT C-D. A FAILURE
    //  HERE MEANS A DEFERRED-DESIGNSYSTEM CONCERN HAS LEAKED ACROSS THE BOUNDARY: the contract has
    //  acquired a member that names or types a presentational primitive, which is the precise failure
    //  mode Correction 3 exists to prevent and which constraint C-D forbids even as a stub. Do not
    //  relax an assertion here. Remove the member, surface its DATA over the published contract, and
    //  name the rendering half as a reserved `/v1/design/**` extension point (AAP 0.4.4).
    // ==============================================================================================

    /// <summary>
    /// The presentational tokens no contract member name and no contract member type may contain.
    /// </summary>
    /// <remarks>
    /// EXPRESSED AS A THEORY SO A FUTURE ADDITION FAILS LOUDLY AND READABLY: the failing token names
    /// itself in the test case, and the message names every offending member. Each token is one of the
    /// DesignSystem primitives AAP 0.2.1.3 Correction 4 and AAP 0.4.4 assign to the deferred
    /// `/v1/design/**` route - the window-handle family, the DPI conversion family, font and canvas
    /// measurement, colour, images, popup menus, the Win32 interop surface and theming.
    /// </remarks>
    public static TheoryData<string> ForbiddenPresentationalTokens() => new()
    {
        // The window-handle family. se_cst_datawindow.sru:L74-L80 reaches
        // `Win32.RedrawWindow(#Handle, ...)`, and n_cst_dwsvc_dropdownsearch.sru:L171, :L214 and :L469
        // reach `Handle(...)`. Every one of them is deferred.
        "Handle",
        "hWnd",

        // The DPI conversion family. n_cst_dwsvc_contextmenu.sru:L1241, :L1243, :L1409 and :L1411 read
        // `PX2MMX(D2PX(...))`; n_cst_dwsvc_columnsort.sru:L359 and :L361 read `PX2MMY(U2PY(10))`.
        "DPI",
        "PX2MM",
        "MM2PX",
        "U2P",
        "D2PX",

        // Font and canvas measurement. n_cst_dwsvc_contextmenu.sru:L1092, :L1098, :L1266 and :L1277
        // create `n_cst_font`. DataServices returns COMPUTED LOGICAL TEXT WIDTHS as data instead.
        "Font",
        "Canvas",
        "Painter",

        // Colour, in both spellings. se_cst_datawindow.sru:L64-L72 overrides `ongetcolor` over
        // `theme.CLR_TRANSPARENT` and `theme.CLR_BKGND`.
        "Colour",
        "Color",

        // Images.
        "Image",

        // Menu RENDERING. n_cst_dwsvc_contextmenu.sru:L18 and :L96-L106 expose an
        // `n_cst_popupmenu`-typed submenu API; the headless half returns the ITEM MODEL instead.
        "PopupMenu",

        // The Win32 interop surface. Four calls in n_cst_dwsvc_dropdownsearch.sru alone, at :L256,
        // :L474, :L475 and :L489.
        "Win32",

        // Theming, which is the ENTIRE content of the dropped parent se_cst_datawindow.sru.
        "Theme",
    };

    [Theory]
    [MemberData(nameof(ForbiddenPresentationalTokens))]
    public void NoContractMemberNamesOrTypesAPresentationalConcern(string token)
    {
        // THE AUDIT: AAP 0.2.1.3 Correction 3 plus constraint C-D. A failure means a
        // deferred-DesignSystem concern has leaked across the boundary. See this region's banner.
        SurfaceMember[] offenders =
        [
            .. Surface().Where(member => member.ScannableText.Any(
                text => text.Contains(token, StringComparison.OrdinalIgnoreCase))),
        ];

        Assert.True(
            offenders.Length == 0,
            "Constraint C-D breach. The DataServices host contract must carry no DesignSystem "
            + "concern, yet the presentational token '"
            + token
            + "' appears on "
            + offenders.Length.ToString(CultureInfo.InvariantCulture)
            + " member(s): "
            + string.Join(", ", offenders.Select(member => member.Locator))
            + ". DesignSystem is a DEFERRED service that must not be implemented even partially and "
            + "even to stub it out. Remove the member and surface its data over the published "
            + "contract, naming the rendering half as a reserved /v1/design/** extension point.");
    }

    /// <summary>
    /// The members the DROPPED PARENT <c>se_cst_datawindow</c> declares or touches, none of which may
    /// appear anywhere on the contract surface.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="ForbiddenPresentationalTokens"/> AND NOT REDUNDANT WITH IT. That theory
    /// forbids a CATEGORY of concern; this one forbids the SPECIFIC members of the specific type
    /// AAP 0.2.1.3 Correction 3 records as REFERENCE-only, which is the narrower and more direct claim.
    /// Each token is precise rather than categorical for a reason: the parent reaches <c>of_Redraw()</c>
    /// and <c>Visible</c>, and a substring scan for "Redraw" or "Visible" would collide with the
    /// legitimately consumed <c>SetRedraw</c> and <c>IsItemVisible</c>, turning a real invariant into a
    /// false alarm.
    /// </remarks>
    public static TheoryData<string> DroppedParentMembers() => new()
    {
        // se_cst_datawindow.sru:L9-L11 - the three theme events.
        "OnThemeRegistering",
        "OnThemeRegistered",
        "OnThemeMgrNotify",

        // :L64-L72 - the colour hook.
        "OnGetColor",

        // :L74-L80 - the redraw-by-handle hook.
        "OnThemeChanged",

        // :L15-L54 and :L82-L90 - the theme manager and control registration.
        "ThemeManager",
        "RegisterControl",
        "UnregisterControl",

        // The DesignSystem members the parent additionally touches.
        "LockUpdate",
        "HSplitScroll",
        "UpdatePoints",
        "Transparent",

        // The parent itself, and the FURTHER DesignSystem ancestor it derives from [:L4, :L8].
        "se_cst_datawindow",
        "s_cst_datawindow",
    };

    [Theory]
    [MemberData(nameof(DroppedParentMembers))]
    public void NoContractMemberIsSourcedFromTheDeferredParent(string parentMember)
    {
        // AAP 0.2.1.3 Correction 3: `se_cst_datawindow` is REFERENCE-only and NOT ONE LINE of it is
        // ported. This is that ruling made executable.
        SurfaceMember[] offenders =
        [
            .. Surface().Where(member => member.ScannableText.Any(
                text => text.Contains(parentMember, StringComparison.OrdinalIgnoreCase))),
        ];

        Assert.True(
            offenders.Length == 0,
            "AAP 0.2.1.3 Correction 3 breach. `se_cst_datawindow` is REFERENCE-only and nothing may be "
            + "ported from it, yet '"
            + parentMember
            + "' appears on: "
            + string.Join(", ", offenders.Select(member => member.Locator))
            + ".");
    }

    [Fact]
    public void TheStructuralInheritanceEdgeIntoDesignSystemIsCutAtTheRoot()
    {
        // se_cst_dw.sru:L4 and :L10 both read `global type se_cst_dw from se_cst_datawindow`, and that
        // parent derives in turn from a further DesignSystem ancestor [se_cst_datawindow.sru:L4, :L8].
        // The .NET contract is the point at which that chain STOPS: both contract classes root directly
        // on System.Object and implement no interface, so there is no ancestor left through which a
        // DesignSystem member could arrive. This is the single most load-bearing fact in the file - the
        // forbidden-token theories police what the contract DECLARES, and this fact polices what it
        // could INHERIT.
        Assert.True(typeof(DataWindowServiceHost).IsAbstract);
        Assert.Equal(typeof(object), typeof(DataWindowServiceHost).BaseType);
        Assert.Empty(typeof(DataWindowServiceHost).GetInterfaces());

        Assert.True(typeof(DataWindowServiceBase).IsAbstract);
        Assert.Equal(typeof(object), typeof(DataWindowServiceBase).BaseType);
        Assert.Empty(typeof(DataWindowServiceBase).GetInterfaces());

        // The three handle interfaces extend nothing either, so the same argument covers them.
        Assert.Empty(typeof(IDataWindowValueBuffer).GetInterfaces());
        Assert.Empty(typeof(IDataWindowObject).GetInterfaces());
        Assert.Empty(typeof(IDataWindowChild).GetInterfaces());
    }

    /// <summary>
    /// The four services AAP 0.2.2.2 defers, which must have no code, no test, no container and no
    /// partial implementation - and therefore no type a contract member could possibly be typed as.
    /// </summary>
    public static TheoryData<string> DeferredServiceNames() => new()
    {
        "DesignSystem",
        "Documents",
        "Integration",
        "ScriptBridge",
    };

    [Theory]
    [MemberData(nameof(DeferredServiceNames))]
    public void NoSurfaceTypeComesFromADeferredService(string deferredService)
    {
        // Constraint C-D at the ASSEMBLY level. The four deferred services are represented in this
        // refactor in exactly two ways and no others (AAP 0.1.1): as destination assignments in the
        // full-estate mapping, and as four reserved Gateway routes returning 501. Neither produces an
        // assembly, so no type on this surface may name one.
        string[] offenders =
        [
            .. Surface()
                .SelectMany(member => member.ScannableText)
                .Where(text => text.Contains(deferredService, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal),
        ];

        Assert.True(
            offenders.Length == 0,
            "Constraint C-D breach. The deferred service '"
            + deferredService
            + "' has no project, no container and no assembly in this refactor, yet the contract "
            + "surface names it in: "
            + string.Join(", ", offenders)
            + ".");
    }

    [Fact]
    public void ContractFileDeclaresExactlySixPublicTypes()
    {
        // ContractTypes above IS the input to every structural invariant in this region, so a seventh
        // public type added to Domain/DataWindowServiceHost.cs without being added there would escape
        // all of them silently. This fact closes that hole by counting the file's public types the way
        // the assembly reports them.
        //
        // Matched by NAMESPACE AND FILE ROSTER rather than by source file, because reflection has no
        // notion of a file. The six are the ones the contract's own header enumerates:
        // IDataWindowValueBuffer, IDataWindowObject, IDataWindowChild, DataWindowServiceHost,
        // DataWindowServiceBase and DataWindowObjectExtensions.
        string[] expected =
        [
            nameof(DataWindowObjectExtensions),
            nameof(DataWindowServiceBase),
            nameof(DataWindowServiceHost),
            nameof(IDataWindowChild),
            nameof(IDataWindowObject),
            nameof(IDataWindowValueBuffer),
        ];

        string[] declared =
        [
            .. ContractTypes.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(expected, declared);
        Assert.All(ContractTypes, type => Assert.True(type.IsPublic));
        Assert.All(
            ContractTypes,
            type => Assert.Equal("PowerFramework.DataServices.Domain", type.Namespace));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE MEASURED ALLOW-LIST: THE MEMBERS `se_cst_dw` AND `n_cst_dwsvc` ACTUALLY CONSUME
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The DataWindow members AAP 0.4.2.5's consumption criterion admits to the host contract, each
    /// measured at a real call site in one of the two ported sources.
    /// </summary>
    /// <remarks>
    /// A POSITIVE CONTAINMENT CHECK, DELIBERATELY NOT AN EXACT-SET CHECK. The contract legitimately
    /// carries more than this list, because three further ported sources contribute measured members -
    /// <c>n_cst_dwsvc_dropdownsearch.sru</c>, <c>n_cst_dwsvc_rowselect.sru</c> and
    /// <c>n_cst_dwsvc_columnsort.sru</c> - and the contract's own header records the call site for each.
    /// Asserting an exact set here would therefore fail on correct code. What matters, and what is
    /// asserted, is that every member the brief measured IS present: a contract that had QUIETLY LOST
    /// one would break a ported call site, and a contract that had quietly gained a presentational one
    /// is caught by the forbidden-token theories above.
    /// </remarks>
    public static TheoryData<string> HostMembersTheOracleConsumes() => new()
    {
        // n_cst_dwsvc.sru (31 uses) + se_cst_dw.sru (6 uses). The dominant member by an order of
        // magnitude: every property read in the service layer funnels through it.
        "Describe",

        // se_cst_dw.sru :L219 :L233 :L235 :L237 :L239 :L241 :L243 :L375 - seven overloads, one per
        // value type the coercion table at :L228-L243 produces, plus the `any` restore path.
        "SetItem",

        // se_cst_dw.sru :L190 :L335 and :L220 :L376.
        "GetItemStatus",
        "SetItemStatus",

        // se_cst_dw.sru :L369 :L421 :L429 :L434 :L441.
        "RowCount",

        // se_cst_dw.sru :L154 :L156 :L425 :L428 :L436, and :L155 for the setter.
        "GetRow",
        "SetRow",

        // se_cst_dw.sru:L554 - -1 means failure.
        "AcceptText",

        // se_cst_dw.sru:L406 and :L431, both as `super::` calls - success is 1, not 0.
        "Filter",
        "DeleteRow",

        // se_cst_dw.sru:L442.
        "SetRedraw",

        // se_cst_dw.sru:L400 is the semantic EVENT `Event GetFocus()`; :L553's `GetFocus() <> this` is
        // the PowerScript system function, renamed GetFocusedObject by the contract's DECISION 3
        // because C# forbids two members differing only in return type. Both are asserted, because
        // dropping either would lose a consumed behaviour.
        "GetFocus",
        "GetFocusedObject",

        // se_cst_dw.sru:L555.
        "SetFocus",

        // The six typed getters. n_cst_dwsvc.sru:L605-L615 and :L621-L631 consume all twelve call sites
        // on the CHILD DataWindow, which is why IDataWindowChild carries them too - see
        // TheSixTypedGettersLiveOnTheChildWhereTheOracleConsumesThem.
        "GetItemString",
        "GetItemDecimal",
        "GetItemNumber",
        "GetItemDateTime",
        "GetItemDate",
        "GetItemTime",

        // n_cst_dwsvc.sru:L119 and :L122 - `#DataWindow.Object` and its
        // `Object.__Get_Attribute(dwoName,false)` lookup.
        "ObjectModel",
        "GetObjectAttribute",

        // n_cst_dwsvc.sru:L651 and :L658.
        "GetValue",

        // n_cst_dwsvc.sru:L592.
        "GetChild",
    };

    [Theory]
    [MemberData(nameof(HostMembersTheOracleConsumes))]
    public void TheHostContractCarriesEveryMemberTheOracleConsumes(string memberName)
    {
        MemberInfo[] declared = typeof(DataWindowServiceHost)
            .GetMember(memberName, DeclaredAtAnyAccessibility);

        Assert.True(
            declared.Length > 0,
            "DataWindowServiceHost no longer declares '"
            + memberName
            + "', which the ported oracle consumes at a measured call site. Removing it breaks a "
            + "ported behaviour; see this theory's remarks for the locator.");

        Assert.All(declared, member => Assert.True(IsExternallyReachableMember(member)));
    }

    /// <summary>
    /// Whether a member reduced from <see cref="MemberInfo"/> is externally reachable, used by the
    /// allow-list theories to confirm a consumed member is not merely present but usable.
    /// </summary>
    private static bool IsExternallyReachableMember(MemberInfo member) => member switch
    {
        MethodBase method => IsReachable(method),
        PropertyInfo property => IsReachable(property.GetMethod) || IsReachable(property.SetMethod),
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        EventInfo => true,
        Type nested => nested.IsNestedPublic || nested.IsNestedFamily,
        _ => false,
    };

    /// <summary>
    /// The <c>dwobject</c> accessors, each measured on <c>se_cst_dw.sru</c>.
    /// </summary>
    /// <remarks>
    /// FIVE, NOT FOUR, AND THE DIFFERENCE IS MEASURED RATHER THAN COSMETIC. The folder brief enumerates
    /// four - <c>ID</c> (12 uses), <c>Name</c> (6), <c>ColType</c> (2) and <c>Primary[row]</c> (7 uses,
    /// at <c>se_cst_dw.sru:L189</c>, <c>:L198</c>, <c>:L200</c>, <c>:L206</c>, <c>:L334</c> and twice on
    /// <c>:L372</c>). The contract declares a FIFTH, <c>Type</c>, on evidence from a third ported source:
    /// <c>n_cst_dwsvc_contextmenu.sru:L242</c> reads
    /// <c>row &gt; 0 and (dwo.Type = "column" or dwo.Type = "compute")</c> and <c>:L245</c> reads
    /// <c>elseif dwo.Type = "compute"</c>, and those two tests decide whether the context menu has one
    /// item or many. <c>Type</c> is the DataWindow OBJECT kind while <c>ColType</c> is the DATABASE type
    /// of a column - two different things that are easy to confuse - so both are carried and both are
    /// asserted here. The contract's own prose header still says "exactly four members"; the DECLARATION
    /// is authoritative, so this suite covers what is declared.
    /// </remarks>
    public static TheoryData<string> DataWindowObjectAccessors() => new()
    {
        "ID",
        "Name",
        "ColType",
        "Type",
        "Primary",
    };

    [Theory]
    [MemberData(nameof(DataWindowObjectAccessors))]
    public void TheDwObjectHandleCarriesEveryAccessorTheOracleReads(string accessor)
    {
        PropertyInfo? property = typeof(IDataWindowObject).GetProperty(accessor);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetMethod);

        // READ ONLY, every one of them. A `dwobject` in the legacy is a handle the runtime hands out;
        // nothing in either ported source assigns through it.
        Assert.Null(property.SetMethod);
    }

    [Fact]
    public void TheDwObjectHandleCarriesNoSixthAccessor()
    {
        // Counted rather than merely enumerated, so a sixth accessor added without a measured call site
        // fails here. AAP 0.4.2.5's criterion is "only the members actually consumed", and a member
        // added because it "should" be on a DataWindow is precisely what that criterion excludes.
        string[] declared =
        [
            .. typeof(IDataWindowObject)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(["ColType", "ID", "Name", "Primary", "Type"], declared);

        // Five PROPERTIES and no method at all - the property accessors reflection reports are special
        // names, so anything that is not one would be a genuine method the oracle never calls.
        Assert.DoesNotContain(typeof(IDataWindowObject).GetMethods(), method => !method.IsSpecialName);
    }

    [Fact]
    public void ThePrimaryBufferViewPreservesTheIndexedRowShape()
    {
        // `dwo.Primary[row]` ports as `dwo.Primary[row]`, which is why the view is an INDEXER rather
        // than a method: preserving the shape keeps every ported expression readable against its
        // oracle line. Reflection names an indexer "Item".
        PropertyInfo? indexer = typeof(IDataWindowValueBuffer).GetProperty("Item");

        Assert.NotNull(indexer);
        ParameterInfo[] parameters = indexer!.GetIndexParameters();
        Assert.Single(parameters);

        // ONE-BASED `long` ROWS. AAP 0.4.5.4 names one-based to zero-based translation the single most
        // dangerous mechanical hazard in this refactor, so the row number stays the legacy `long`.
        Assert.Equal(typeof(long), parameters[0].ParameterType);

        // `object?`, because a buffer value may legitimately be null: se_cst_dw.sru:L198-L202 carries an
        // explicit `IsNull(aOrgValue) and IsNull(dwo.Primary[row])` arm whose whole purpose is to
        // classify null-versus-null as EQUAL. AAP 0.4.5.4 forbids collapsing null to zero.
        Assert.Equal(typeof(object), indexer.PropertyType);

        // GET ONLY on the contract. The concrete double implements a get/set indexer so a suite can
        // stage a mid-flight mutation, but the contract itself grants no write.
        Assert.NotNull(indexer.GetMethod);
        Assert.Null(indexer.SetMethod);
    }

    /// <summary>
    /// The six typed getters, asserted on the CHILD DataWindow because that is where the oracle
    /// consumes them.
    /// </summary>
    /// <remarks>
    /// The contract's DECISION 2 measured all twelve call sites onto <c>dwc</c>, the
    /// <c>datawindowchild</c> obtained from <c>#DataWindow.GetChild</c> -
    /// <c>n_cst_dwsvc.sru:L605</c>, <c>:L607</c>, <c>:L609</c>, <c>:L611</c>, <c>:L613</c>, <c>:L615</c>,
    /// <c>:L621</c>, <c>:L623</c>, <c>:L625</c>, <c>:L627</c>, <c>:L629</c> and <c>:L631</c>, all inside
    /// <c>_of_getcolumnvaluemap</c> - and records that there is not one <c>#DataWindow.GetItem*</c> call
    /// anywhere in either ported source. The host carries the six as well, for the attached services
    /// added later; both homes are therefore asserted rather than one being treated as the mistake.
    /// </remarks>
    public static TheoryData<string, string> ChildTypedGetters() => new()
    {
        // n_cst_dwsvc.sru:L605, :L621 - the display value, which the oracle skips the row on when null
        // [:L617].
        { "GetItemString", "System.String" },

        // :L609, :L625.
        { "GetItemDecimal", "System.Decimal" },

        // :L607, :L623.
        { "GetItemNumber", "System.Double" },

        // :L611, :L627.
        { "GetItemDateTime", "System.DateTime" },

        // :L613, :L629.
        { "GetItemDate", "System.DateOnly" },

        // :L615, :L631.
        { "GetItemTime", "System.TimeOnly" },
    };

    [Theory]
    [MemberData(nameof(ChildTypedGetters))]
    public void TheSixTypedGettersLiveOnTheChildWhereTheOracleConsumesThem(
        string getter,
        string underlyingReturnType)
    {
        MethodInfo? onChild = typeof(IDataWindowChild).GetMethod(getter);
        Assert.NotNull(onChild);

        // ADDRESSED BY COLUMN NAME, not by column number: n_cst_dwsvc.sru threads the column NAME
        // through `_of_getcolumnvaluemap`.
        ParameterInfo[] parameters = onChild!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(long), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);

        // ALL SIX RETURN A NULLABLE TYPE, and that is contract rather than caution. The oracle tests
        // `if IsNull(sDispVal) then continue` [:L617] and `if IsNull(aVal) then continue` [:L633], so a
        // null item is an ORDINARY outcome the caller discriminates on. A non-nullable return would
        // make both `continue` arms unreachable, and AAP 0.4.5.4 forbids collapsing null to zero
        // because that silently converts a skipped row into a mapped one.
        Type returnType = onChild.ReturnType;
        if (returnType.IsValueType)
        {
            Type? underlying = Nullable.GetUnderlyingType(returnType);
            Assert.NotNull(underlying);
            Assert.Equal(underlyingReturnType, underlying!.FullName);
        }
        else
        {
            // `string?` is a reference type, so nullability is an annotation rather than a wrapper.
            Assert.Equal(underlyingReturnType, returnType.FullName);
        }

        // The host declares the same six by column name, for the attached services ported after
        // DECISION 2 was recorded. Both homes exist and both are legitimate.
        Assert.NotNull(typeof(DataWindowServiceHost).GetMethod(getter, [typeof(long), typeof(string)]));
    }

    [Fact]
    public void SetRedrawIsTheOneBorderlineMemberAndItCarriesNoGeometry()
    {
        // WORTH ITS OWN TEST, BECAUSE IT IS THE ONE MEMBER A READER WILL QUESTION. `SetRedraw` sounds
        // presentational, and se_cst_dw.sru:L440-L443 even comments that it refreshes the DETAIL band's
        // colour. It stays on the contract, and the reason is measurable rather than a judgement call:
        // the call at :L442 takes ONE BOOLEAN and nothing else - no geometry, no DPI value, no font
        // metric, no colour and no window handle. Dropping it would remove an observable call the oracle
        // makes; implementing the colour refresh would be DesignSystem work. A headless implementation
        // is a faithful no-op rather than a stub, so constraint C-D is not engaged.
        MethodInfo? setRedraw = typeof(DataWindowServiceHost).GetMethod(nameof(DataWindowServiceHost.SetRedraw));

        Assert.NotNull(setRedraw);
        ParameterInfo[] parameters = setRedraw!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(bool), parameters[0].ParameterType);

        // Success is 1, not 0 - it is a DataWindow function return, never a RetCode.
        Assert.Equal(typeof(int), setRedraw.ReturnType);
    }

    // ==============================================================================================
    //  PHASE 2 - THE SEMANTIC EVENT SURFACE: EXACTLY ELEVEN
    //  --------------------------------------------------------------------------------------------
    //  `se_cst_dw` declares 22 events in total: 13 raw `pbm_dwn*` events and 9 events of its own
    //  [se_cst_dw.sru:L11-L32]. That 22-event chain belongs to Domain/DataWindowEventChain.cs and is
    //  characterized by DataWindowEventChainTests. What belongs HERE is different and complementary:
    //  the ELEVEN SEMANTIC EVENTS `se_cst_dw` RAISES ON ITS OWN ANCESTRY, each of which the .NET
    //  contract has to declare because the ancestry that used to declare them was dropped.
    //
    //  The eleven are exactly the events reached through `Event <Name>(...)` in se_cst_dw.sru:
    //      RButtonDown        :L115      ItemChanged        :L292
    //      RowFocusChanged    :L125      ItemError          :L343
    //      RowFocusChanging   :L131      LoseFocus          :L392
    //      DoubleClicked      :L136      GetFocus           :L400
    //      Clicked            :L145
    //      EditChanged        :L164
    //      ItemFocusChanged   :L177
    // ==============================================================================================

    /// <summary>
    /// The six events <c>se_cst_dw</c> declares FOR ITSELF, which are framework hooks rather than
    /// ancestry semantic events and are therefore excluded when the eleven are counted.
    /// </summary>
    /// <remarks>
    /// Declared at <c>se_cst_dw.sru:L11</c> (<c>oninitcontextmenu</c>), <c>:L12</c>
    /// (<c>oncontextmenu</c>), <c>:L13</c> (<c>onddsgetfilter</c>), <c>:L24</c>
    /// (<c>ondoitemchange</c>), <c>:L26</c> (<c>ondoitemchanged</c>) and <c>:L28</c>
    /// (<c>onddsfiltered</c>). The distinction is not cosmetic: an ancestry event is one the DataWindow
    /// control itself would have raised, so the contract MUST declare it or the ported chain has
    /// nothing to raise; a framework hook is one `se_cst_dw` invented, and it appears on the contract
    /// only because an attached service subscribes to it.
    /// </remarks>
    private static readonly string[] FrameworkHooksDeclaredBySeCstDwItself =
    [
        "OnInitContextMenu",
        "OnContextMenu",
        "OnDDSGetFilter",
        "OnDDSFiltered",
        "OnDoItemChange",
        "OnDoItemChanged",
    ];

    /// <summary>
    /// The two built-in DataWindow FUNCTIONS <c>se_cst_dw</c> overrides, which are virtual on the
    /// contract for the reason its DECISION 5 records and are not events at all.
    /// </summary>
    /// <remarks>
    /// <c>se_cst_dw.sru:L403-L407</c> overrides <c>filter</c> and calls <c>super::Filter()</c>;
    /// <c>:L431</c> does the same for <c>DeleteRow(nRow)</c>. Both are virtual over a protected abstract
    /// <c>*Core</c> operation precisely so `base.` can reach the built-in behaviour, which a plain
    /// abstract member could not offer. Both return an integer where SUCCESS IS 1 - never a RetCode.
    /// </remarks>
    private static readonly string[] BuiltInFunctionOverrides = ["Filter", "DeleteRow"];

    /// <summary>
    /// The semantic ancestry events the contract actually declares, computed rather than transcribed so
    /// the "exactly eleven" claim is a measurement instead of a restatement.
    /// </summary>
    private static IReadOnlyList<string> DeclaredSemanticEventNames() =>
    [
        .. typeof(DataWindowServiceHost)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.IsVirtual && !method.IsAbstract && !method.IsSpecialName)
            .Select(method => method.Name)
            .Where(name => !FrameworkHooksDeclaredBySeCstDwItself.Contains(name, StringComparer.Ordinal))
            .Where(name => !BuiltInFunctionOverrides.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

    [Fact]
    public void TheContractExposesExactlyElevenSemanticDataWindowEvents()
    {
        // Sorted ordinally so the failure diff is stable and readable. Every name is transcribed from
        // its `Event <Name>(...)` call site in se_cst_dw.sru; the locators are in this region's banner.
        string[] expected =
        [
            "Clicked",           // :L145
            "DoubleClicked",     // :L136
            "EditChanged",       // :L164
            "GetFocus",          // :L400
            "ItemChanged",       // :L292
            "ItemError",         // :L343
            "ItemFocusChanged",  // :L177
            "LoseFocus",         // :L392
            "RButtonDown",       // :L115
            "RowFocusChanged",   // :L125
            "RowFocusChanging",  // :L131
        ];

        Assert.Equal(expected, DeclaredSemanticEventNames());
        Assert.Equal(11, DeclaredSemanticEventNames().Count);
    }

    /// <summary>
    /// The signature of each of the eleven, so a widened or narrowed parameter list fails as loudly as
    /// a missing event would.
    /// </summary>
    public static TheoryData<string, string, string> SemanticEventSignatures() => new()
    {
        // se_cst_dw.sru:L115 - `Event RButtonDown(xpos,ypos,row,dwo)`.
        {
            "RButtonDown",
            "System.Int64",
            "System.Int64|System.Int64|System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject"
        },

        // :L125 - `Event RowFocusChanged(currentRow)`.
        { "RowFocusChanged", "System.Int64", "System.Int64" },

        // :L131 - `Event RowFocusChanging(currentrow,newrow)`.
        { "RowFocusChanging", "System.Int64", "System.Int64|System.Int64" },

        // :L136 - `Event DoubleClicked(xpos,ypos,row,dwo)`.
        {
            "DoubleClicked",
            "System.Int64",
            "System.Int64|System.Int64|System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject"
        },

        // :L145 - `Event Clicked(xpos,ypos,row,dwo)`.
        {
            "Clicked",
            "System.Int64",
            "System.Int64|System.Int64|System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject"
        },

        // :L164 - `Event EditChanged(row,dwo,data)`.
        {
            "EditChanged",
            "System.Int64",
            "System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject|System.String"
        },

        // :L177 - `Event ItemFocusChanged(row,dwo)`.
        {
            "ItemFocusChanged",
            "System.Int64",
            "System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject"
        },

        // :L292 - `return Event ItemChanged(row,dwo,data)`.
        {
            "ItemChanged",
            "System.Int64",
            "System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject|System.String"
        },

        // :L343 - `rtCode = Event ItemError(row,dwo,data)`. THE ONE NULLABLE RETURN; see
        // ItemErrorMayAnswerNullSoTheOraclesCoercionStaysReachable for why.
        {
            "ItemError",
            "System.Nullable<System.Int64>",
            "System.Int64|PowerFramework.DataServices.Domain.IDataWindowObject|System.String"
        },

        // :L392 - `return Event LoseFocus()`.
        { "LoseFocus", "System.Int64", "" },

        // :L400 - `return Event GetFocus()`.
        { "GetFocus", "System.Int64", "" },
    };

    [Theory]
    [MemberData(nameof(SemanticEventSignatures))]
    public void EachSemanticEventKeepsItsOracleSignature(
        string eventName,
        string returnType,
        string parameterTypes)
    {
        MethodInfo? declared = typeof(DataWindowServiceHost)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SingleOrDefault(method => string.Equals(method.Name, eventName, StringComparison.Ordinal));

        Assert.NotNull(declared);

        // VIRTUAL AND NOT ABSTRACT. A PowerBuilder event with no script attached still returns - it
        // answers the type's default - so every one of the eleven carries a base implementation the
        // derived chain may override rather than must.
        Assert.True(declared!.IsVirtual);
        Assert.False(declared.IsAbstract);

        Assert.Equal(returnType, Spell(declared.ReturnType));
        Assert.Equal(
            parameterTypes,
            string.Join("|", declared.GetParameters().Select(p => Spell(p.ParameterType))));
    }

    /// <summary>
    /// The two raw mouse-release events that fire the broker and NOTHING ELSE, so no semantic
    /// counterpart may exist for either.
    /// </summary>
    /// <remarks>
    /// THIS ASYMMETRY IS EASY TO "TIDY UP" INTO SYMMETRY, AND DOING SO WOULD BE A BEHAVIOURAL CHANGE.
    /// `ondwnrbuttondown` [<c>se_cst_dw.sru:L115-L118</c>] raises the semantic event AND THEN the
    /// broker, so a reader naturally expects `ondwnrbuttonup` to do the same. It does not:
    /// <c>:L120-L122</c> is three lines long and its only statement is
    /// <c>if Eventful.of_Trigger(EVT_RBUTTONUP,xpos,ypos,row,dwo) = 1 then return 1</c>.
    /// <c>:L395-L397</c> is identical for <c>ondwnlbuttonup</c> and <c>EVT_LBUTTONUP</c>. Inventing
    /// either semantic event would fabricate a hook the oracle does not have, and a subscriber written
    /// against the fabricated hook would be silently unreachable in the legacy.
    /// </remarks>
    public static TheoryData<string, string> BrokerOnlyReleaseEvents() => new()
    {
        // se_cst_dw.sru:L120-L122 - `ondwnrbuttonup`, broker only.
        { "RButtonUp", "se_cst_dw.sru:L120-L122 (ondwnrbuttonup fires EVT_RBUTTONUP and nothing else)" },

        // se_cst_dw.sru:L395-L397 - `ondwnlbuttonup`, broker only.
        { "LButtonUp", "se_cst_dw.sru:L395-L397 (ondwnlbuttonup fires EVT_LBUTTONUP and nothing else)" },
    };

    [Theory]
    [MemberData(nameof(BrokerOnlyReleaseEvents))]
    public void NoSemanticReleaseEventExistsForEitherMouseButton(string eventName, string locator)
    {
        // Inherited members are included on purpose - no DeclaredOnly - so the assertion also proves the
        // event cannot arrive from a base type.
        MemberInfo[] found = typeof(DataWindowServiceHost).GetMember(
            eventName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        Assert.True(
            found.Length == 0,
            "The oracle raises no semantic "
            + eventName
            + " event - "
            + locator
            + " - so declaring one on the contract fabricates a hook the legacy does not have. This "
            + "asymmetry with RButtonDown is deliberate legacy behaviour (constraint C-B).");

        // And nothing anywhere on the surface smuggles it in under a longer name.
        Assert.DoesNotContain(
            Surface(),
            member => member.Name.Contains(eventName, StringComparison.Ordinal));
    }

    [Fact]
    public void ItemErrorMayAnswerNullSoTheOraclesCoercionStaysReachable()
    {
        // se_cst_dw.sru:L342-L345 reads:
        //     if rtCode = 0 then
        //         rtCode = Event ItemError(row,dwo,data)
        //         if IsNull(rtCode) then rtCode = 0
        //     end if
        //
        // That third line only means something if ItemError CAN answer null, which in PowerBuilder it
        // can because an event with no script attached returns null for a `long` return. The contract
        // therefore types the return `long?` AND defaults it to null - not to 0. A contract that
        // defaulted to 0 would make the ported coercion dead code and silently delete a branch, which
        // is exactly the class of regression constraint C-B and goal G2 forbid.
        MethodInfo itemError = typeof(DataWindowServiceHost)
            .GetMethod(nameof(DataWindowServiceHost.ItemError))!;

        Assert.Equal(typeof(long?), itemError.ReturnType);
        Assert.Equal(typeof(long), Nullable.GetUnderlyingType(itemError.ReturnType));

        FakeDataWindowHost host = NewHostWithOneColumn();
        IDataWindowObject dwo = host.DwObject(ProbeColumn);

        long? unscripted = host.ItemError(OneBasedRows.FirstRow, dwo, "bad");
        Assert.Null(unscripted);

        // The coercion the oracle performs AT THE CALL SITE, reproduced here to show it is reachable.
        long coerced = unscripted ?? 0L;
        Assert.Equal(0L, coerced);

        // And the ten others answer 0 rather than null, which is why only ItemError is nullable.
        Assert.Equal(0L, host.RButtonDown(1L, 2L, OneBasedRows.FirstRow, dwo));
        Assert.Equal(0L, host.RowFocusChanged(OneBasedRows.FirstRow));
        Assert.Equal(0L, host.RowFocusChanging(OneBasedRows.FirstRow, 2L));
        Assert.Equal(0L, host.DoubleClicked(1L, 2L, OneBasedRows.FirstRow, dwo));
        Assert.Equal(0L, host.Clicked(1L, 2L, OneBasedRows.FirstRow, dwo));
        Assert.Equal(0L, host.EditChanged(OneBasedRows.FirstRow, dwo, "typed"));
        Assert.Equal(0L, host.ItemFocusChanged(OneBasedRows.FirstRow, dwo));
        Assert.Equal(0L, host.ItemChanged(OneBasedRows.FirstRow, dwo, "typed"));
        Assert.Equal(0L, host.LoseFocus());
        Assert.Equal(0L, host.GetFocus());
    }

    [Fact]
    public void TheElevenSemanticEventsAreDistinctFromTheSixFrameworkHooksAndTheTwoOverrides()
    {
        // 11 + 6 + 2 = 19, and the arithmetic is asserted rather than asserted-in-prose so that adding
        // a virtual member to the contract forces a deliberate decision about which of the three
        // populations it joins.
        string[] allVirtual =
        [
            .. typeof(DataWindowServiceHost)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsVirtual && !method.IsAbstract && !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal),
        ];

        Assert.Equal(19, allVirtual.Length);
        Assert.Equal(11, DeclaredSemanticEventNames().Count);

        foreach (string hook in FrameworkHooksDeclaredBySeCstDwItself)
        {
            Assert.Contains(hook, allVirtual);
            Assert.DoesNotContain(hook, DeclaredSemanticEventNames());
        }

        foreach (string over in BuiltInFunctionOverrides)
        {
            Assert.Contains(over, allVirtual);
            Assert.DoesNotContain(over, DeclaredSemanticEventNames());

            // DECISION 5: virtual over a PROTECTED ABSTRACT `*Core` operation, so `base.Filter()` and
            // `base.DeleteRow(row)` can reach the built-in DataWindow behaviour the way
            // `super::Filter()` [se_cst_dw.sru:L406] and `super::DeleteRow(nRow)` [:L431] do.
            MethodInfo? core = typeof(DataWindowServiceHost).GetMethod(
                over + "Core",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(core);
            Assert.True(core!.IsFamily);
            Assert.True(core.IsAbstract);

            // SUCCESS IS 1, NOT 0. They are DataWindow function returns and are deliberately NOT mapped
            // onto RetCode, whose OK is 0 - conflating the two inverts every success test.
            Assert.Equal(typeof(int), core.ReturnType);
        }
    }

    // ==============================================================================================
    //  PHASE 3 - `SetEnabled` AND THE `OnEnable` VETO
    //  --------------------------------------------------------------------------------------------
    //  n_cst_dwsvc.sru:L89-L95, quoted in full because every line below is asserted:
    //
    //      public function long of_setenabled (readonly boolean benabled);
    //          if #Enabled = bEnabled then return RetCode.OK      // :L89 idempotent early-out
    //          if Event OnEnable(bEnabled) = 1 then return RetCode.FAILED   // :L90 the veto
    //          #Enabled = bEnabled                                // :L92 reached only when allowed
    //          return RetCode.OK                                  // :L94
    //      end function
    // ==============================================================================================

    /// <summary>The one column every probe host needs, so its name is stated once.</summary>
    private const string ProbeColumn = "probe";

    /// <summary>
    /// A minimal host double with one numeric column, for the assertions that need a
    /// <see cref="IDataWindowObject"/> but nothing else from a fixture.
    /// </summary>
    private static FakeDataWindowHost NewHostWithOneColumn()
    {
        FakeDataWindowHost host = new();
        _ = host.AddColumn(ProbeColumn, FakeColumnType.Number);
        _ = host.AddRow(1L);
        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// A host double seeded from the primary fixture, attached to a recording service double that
    /// shares its call log so host reads and service calls interleave in one sequence.
    /// </summary>
    private static (FakeDataWindowHost Host, FakeDataWindowService Service) NewAttachedCompanyFixture()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateCompanyFixture();
        FakeDataWindowService service = new(host.CallLog);
        service.OnInit(host);
        host.CallLog.Clear();
        return (host, service);
    }

    [Fact]
    public void OnInitBindsTheHostAndLiftsTheBrokerOffIt()
    {
        // n_cst_dwsvc.sru:L85-L87:
        //     event oninit(se_cst_dw dw);#DataWindow = dw
        //     #Eventful = dw.Eventful
        //
        // Two assignments, and the second is the one worth a test: the broker is LIFTED OFF THE HOST,
        // not created here. Every attached service therefore shares ONE broker with its host, which is
        // what makes the ordered subscription chain in Domain/DataWindowEventChain.cs coherent - a
        // service that minted its own broker would publish into a topic space nobody subscribes to.
        //
        // TWO HOSTS, SO THE LIFT IS PROVEN RATHER THAN INFERRED. Each host double owns its own broker,
        // so asserting that the service holds the ATTACHED host's broker and not the other host's rules
        // out both alternatives at once: a service that minted its own, and a service that happened to
        // share a process-wide singleton.
        FakeDataWindowHost attached = NewHostWithOneColumn();
        FakeDataWindowHost other = NewHostWithOneColumn();
        FakeDataWindowService service = new();

        Assert.NotSame(attached.Eventful, other.Eventful);
        Assert.Null(service.DataWindow);
        Assert.Null(service.Eventful);

        service.OnInit(attached);

        Assert.Same(attached, service.DataWindow);
        Assert.Same(attached.Eventful, service.Eventful);
        Assert.NotSame(other.Eventful, service.Eventful);
        Assert.Contains("Event OnInit", service.CallLog.Members);

        // BOTH OR NEITHER, WITH NO PARTIALLY ATTACHED STATE. :L85 and :L86 are consecutive and
        // unconditional, so re-attaching moves the host reference AND the broker reference together. A
        // port that moved only the host would leave the two pointing at different objects, which is a
        // state the legacy cannot reach.
        service.OnInit(other);

        Assert.Same(other, service.DataWindow);
        Assert.Same(other.Eventful, service.Eventful);
        Assert.NotSame(attached.Eventful, service.Eventful);
    }

    [Fact]
    public void OnInitRejectsAMissingHostRatherThanAttachingToNothing()
    {
        // FAIL FAST, NEVER GRACEFUL DEGRADATION (AAP 0.1.4). The legacy raises `oninit` with a live
        // `se_cst_dw` during `se_cst_dw`'s own construction [se_cst_dw.sru:L576-L580], so a null host is
        // a structural fault rather than a data condition. Softening this into a warning-and-continue
        // would be a behavioural change dressed as robustness.
        FakeDataWindowService service = new();

        Assert.Throws<ArgumentNullException>(() => service.OnInit(null!));
        Assert.Null(service.DataWindow);
        Assert.Null(service.Eventful);
    }

    /// <summary>
    /// The two attachment references, both of which the legacy declares <c>privatewrite</c>.
    /// </summary>
    public static TheoryData<string> PrivateWriteAttachmentReferences() => new()
    {
        // n_cst_dwsvc.sru:L37 - `privatewrite se_cst_dw #DataWindow`.
        nameof(DataWindowServiceBase.DataWindow),

        // n_cst_dwsvc.sru:L38 - `privatewrite n_cst_eventful #Eventful`.
        nameof(DataWindowServiceBase.Eventful),
    };

    [Theory]
    [MemberData(nameof(PrivateWriteAttachmentReferences))]
    public void TheAttachmentReferencesAreReadOnlyFromOutside(string reference)
    {
        // `privatewrite` in PowerScript means readable everywhere, writable only by the declaring type.
        // The .NET equivalent is a public getter with a private setter, and asserting it matters because
        // a public setter would let a caller re-point a service at a different host WITHOUT the broker
        // being re-lifted - leaving DataWindow and Eventful referring to two different objects, which
        // is a state the legacy cannot reach.
        PropertyInfo property = typeof(DataWindowServiceBase).GetProperty(reference)!;

        Assert.NotNull(property.GetMethod);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.NotNull(property.SetMethod);
        Assert.True(property.SetMethod!.IsPrivate);
    }

    [Fact]
    public void TheEnabledFlagIsProtectedWriteExactlyAsTheLegacyDeclaresIt()
    {
        // n_cst_dwsvc.sru:L39 - `protectedwrite boolean #Enabled`. Wider than the two references above
        // and narrower than public: a DERIVED service may set it, an outside caller may not. That is
        // what makes `SetEnabled` the only externally reachable route into the state, and therefore what
        // makes the veto at :L90 impossible to bypass from outside.
        PropertyInfo enabled = typeof(DataWindowServiceBase)
            .GetProperty(nameof(DataWindowServiceBase.Enabled))!;

        Assert.True(enabled.GetMethod!.IsPublic);
        Assert.True(enabled.SetMethod!.IsFamily);
        Assert.Equal(typeof(bool), enabled.PropertyType);

        // And it starts false, because a PowerBuilder boolean instance variable starts false.
        Assert.False(new FakeDataWindowService().Enabled);
    }

    [Fact]
    public void ThePermittedStateChangeReturnsOkAndIsObservableAfterwards()
    {
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.CallLog.Clear();

        // :L90 with a hook that allows, then :L92 and :L94.
        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.Equal(0L, RetCode.OK);
        Assert.True(service.Enabled);

        // The hook WAS raised - the early-out at :L89 did not apply, because false != true.
        Assert.Contains("Event OnEnable", service.CallLog.Members);
        Assert.Contains("Event OnEnable[true]", service.CallLog.Descriptions);

        // And the reverse direction behaves identically, which is why :L89 tests equality rather than
        // testing for the enabled state specifically.
        service.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.False(service.Enabled);
        Assert.Contains("Event OnEnable[false]", service.CallLog.Descriptions);
    }

    [Fact]
    public void TheStateChangesOnlyAfterTheHookHasPermittedIt()
    {
        // THE ORDERING IS THE BEHAVIOUR. :L90 raises the hook and :L92 assigns, in that order, so the
        // hook is asked about a state that has NOT been adopted yet. A port that assigned first and
        // rolled back on a veto would be observationally different from inside the hook, and the hook is
        // application code in the legacy - it can read `#Enabled`.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();

        bool? requestedInsideHook = null;
        bool? adoptedInsideHook = null;
        service.OnEnableHandler = requested =>
        {
            requestedInsideHook = requested;
            adoptedInsideHook = service.Enabled;
            return 0L;
        };

        Assert.Equal(RetCode.OK, service.SetEnabled(true));

        Assert.True(requestedInsideHook);
        Assert.False(adoptedInsideHook);
        Assert.True(service.Enabled);
    }

    [Fact]
    public void AVetoedStateChangeReturnsFailedAndNotPrevent()
    {
        // ================================================================================
        // THE SHARPEST PRESERVED QUIRK IN THIS FILE (constraint C-B, goal G2)
        // ================================================================================
        // n_cst_dwsvc.sru:L90 reads
        //     if Event OnEnable(bEnabled) = 1 then return RetCode.FAILED
        // The HOOK signals with the bare numeral 1, and the FUNCTION answers -1. A prevention on the
        // way in becomes a FAILURE on the way out, so a caller testing for prevention will never see
        // the veto. It looks like a mistake. It is legacy behaviour and it is reproduced verbatim.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.OnEnableHandler = _ => 1L;

        long answer = service.SetEnabled(true);

        Assert.Equal(RetCode.FAILED, answer);
        Assert.Equal(-1L, answer);

        // Stated the other way round as well, because this is the confusion the quirk invites.
        Assert.NotEqual(RetCode.PREVENT, answer);
        Assert.Equal(1L, RetCode.PREVENT);

        // :L92 was never reached.
        Assert.False(service.Enabled);
    }

    [Fact]
    public void TheVetoedAnswerLandsOnTheFailingSideOfTheTriStatePredicateAlgebra()
    {
        // WHY THE QUIRK ABOVE MATTERS TWICE OVER. FAILED and PREVENT sit on OPPOSITE SIDES of the
        // tri-state algebra in PowerFramework.Shared.Kernel, so confusing them does not merely relabel
        // the answer - it INVERTS the caller's success test:
        //
        //     issucceeded.srf:L11-L13   `return rtCode >= 0`               so PREVENT (1) SUCCEEDS
        //     isfailed.srf:L11-L13      `rtCode < 0 and rtCode <> CANCELLED`  so FAILED (-1) FAILS
        //
        // This assertion pins the interaction on the REAL vetoed answer rather than on the constants,
        // so it cannot pass while the mapping is wrong.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.OnEnableHandler = _ => 1L;

        long answer = service.SetEnabled(true);

        Assert.False(Predicates.IsSucceeded(answer));
        Assert.True(Predicates.IsFailed(answer));

        // The counterfactual: had the veto been mapped to PREVENT, both predicates would flip. That is
        // the tri-state hole AAP 0.8.2 names - a prevention reads as a SUCCESS.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));

        // And a permitted change is unambiguously a success on both readings.
        service.OnEnableHandler = null;
        long permitted = service.SetEnabled(true);
        Assert.Equal(RetCode.OK, permitted);
        Assert.True(Predicates.IsSucceeded(permitted));
        Assert.False(Predicates.IsFailed(permitted));
    }

    /// <summary>
    /// Hook signals and whether each vetoes, driving the point that <c>:L90</c> tests <c>= 1</c> and
    /// not "non-zero" and not "is failure".
    /// </summary>
    public static TheoryData<long, bool> HookSignals() => new()
    {
        // The one and only veto signal [n_cst_dwsvc.sru:L90].
        { 1L, true },

        // Allowed: the natural "no objection" answer, which is also what an unscripted PowerBuilder
        // event returning `long` yields once coerced.
        { 0L, false },

        // Allowed, and this is the counter-intuitive one worth locking down: a hook that answers
        // RetCode.FAILED does NOT veto, because -1 is not 1.
        { -1L, false },

        // Allowed: any other positive value, including RetCode.PREVENT's neighbours.
        { 2L, false },
        { 100L, false },

        // Allowed: CANCELLED, which is neither succeeded nor failed under the algebra, is also not 1.
        { -2L, false },

        // Allowed: the extremes, so the test cannot pass by accident of a narrow numeric range.
        { long.MaxValue, false },
        { long.MinValue, false },
    };

    [Theory]
    [MemberData(nameof(HookSignals))]
    public void OnlyTheExactSignalOneVetoes(long signal, bool vetoes)
    {
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.OnEnableHandler = _ => signal;

        long answer = service.SetEnabled(true);

        if (vetoes)
        {
            Assert.Equal(RetCode.FAILED, answer);
            Assert.False(service.Enabled);
        }
        else
        {
            Assert.Equal(RetCode.OK, answer);
            Assert.True(service.Enabled);
        }
    }

    [Fact]
    public void TheIdempotentEarlyOutSucceedsWithoutRaisingTheHookAtAll()
    {
        // n_cst_dwsvc.sru:L89 - `if #Enabled = bEnabled then return RetCode.OK`. Success, and the hook
        // is NOT raised. Proving it requires reaching the state without going through `of_setenabled`,
        // which is exactly what the double's ForceEnabled exists for: the legacy setter is
        // `protectedwrite`, so a derived type may write it, and that is the access being exercised.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.ForceEnabled(true);
        service.CallLog.Clear();

        // A hook that WOULD veto, so a passing assertion cannot be explained by the hook allowing.
        service.OnEnableHandler = _ => 1L;

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.True(service.Enabled);
        Assert.DoesNotContain("Event OnEnable", service.CallLog.Members);

        // The same holds in the disabled direction.
        service.ForceEnabled(false);
        service.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.False(service.Enabled);
        Assert.DoesNotContain("Event OnEnable", service.CallLog.Members);
    }

    [Fact]
    public void TheUnscriptedHookAllowsBecauseAnUnscriptedPowerBuilderEventObjectsToNothing()
    {
        // The base implementation of `OnEnable` answers 0, which is not 1, so it allows. That is the
        // faithful default: a PowerBuilder event with no script attached raises no objection, and
        // `n_cst_dwsvc` itself ships no `onenable` script - the hook exists purely for a DERIVED service
        // to override [n_cst_dwsvc.sru:L10 declares it, and no body follows].
        MethodInfo onEnable = typeof(DataWindowServiceBase).GetMethod(
            "OnEnable",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.True(onEnable.IsFamily);
        Assert.True(onEnable.IsVirtual);
        Assert.False(onEnable.IsAbstract);
        Assert.Equal(typeof(long), onEnable.ReturnType);

        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();
        service.OnEnableHandler = null;

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.True(service.Enabled);
    }

    [Fact]
    public void SetEnabledTouchesNoHostMemberAndSoWorksBeforeAttachment()
    {
        // MEASURED FROM THE ORACLE RATHER THAN ASSUMED. n_cst_dwsvc.sru:L89-L95 reads `#Enabled` twice
        // and raises `OnEnable` once; it never touches `#DataWindow`. The enablement protocol is
        // therefore independent of attachment, and a port that had routed it through the host would
        // introduce a fail-fast fault the legacy does not have.
        FakeDataWindowService detached = new();

        Assert.Null(detached.DataWindow);
        Assert.Equal(RetCode.OK, detached.SetEnabled(true));
        Assert.True(detached.Enabled);

        // While every genuinely host-facing member DOES fail fast when unattached.
        Assert.Throws<InvalidOperationException>(() => detached.RequireAttachedHost());
        Assert.Throws<InvalidOperationException>(() => detached.ResolveDataWindowObject(ProbeColumn));
        Assert.Throws<InvalidOperationException>(() => detached.ResolveDataWindowObject(1L));
    }

    // ==============================================================================================
    //  PHASE 4 - THE VALUE-LOCKED CONSTANT SETS
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.4.5.3 is the reason these are asserted by NAME as well as by value: the SCREAMING_SNAKE
    //  identifiers travel in serialized payloads, in log records and in characterization recordings, so
    //  a rename would not restyle a symbol - it would silently invalidate every stored comparison that
    //  mentions it. Reflecting on the field name pins the spelling; referencing the constant directly
    //  pins it again at compile time. Both are done.
    // ==============================================================================================

    /// <summary>
    /// The declared <c>public const long</c> catalogue entries whose names share a prefix, ordered so a
    /// failure diff is stable.
    /// </summary>
    private static IReadOnlyList<FieldInfo> DeclaredCatalogue(string prefix) =>
    [
        .. typeof(DataWindowServiceBase)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(long))
            .Where(field => field.Name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(field => field.Name, StringComparer.Ordinal),
    ];

    /// <summary>Reads a catalogue entry's compile-time value.</summary>
    private static long CatalogueValue(FieldInfo field) =>
        Assert.IsType<long>(field.GetRawConstantValue());

    /// <summary>
    /// The data-source presentation styles, transcribed from <c>n_cst_dwsvc.sru:L18-L24</c> together
    /// with the legacy's own trailing comment for each.
    /// </summary>
    public static TheoryData<string, long> PresentationStyleCatalogue() => new()
    {
        // :L18 - constant long STYLE_DEFAULT   = 0 //Form, group, query, or tabular
        { "STYLE_DEFAULT", 0L },

        // :L19 - constant long STYLE_GRID      = 1 //Grid
        { "STYLE_GRID", 1L },

        // :L20 - constant long STYLE_LABEL     = 2 //Label
        { "STYLE_LABEL", 2L },

        // :L21 - constant long STYLE_GRAPH     = 3 //Graph
        { "STYLE_GRAPH", 3L },

        // :L22 - constant long STYLE_CROSSTAB  = 4 //Crosstab
        { "STYLE_CROSSTAB", 4L },

        // :L23 - constant long STYLE_COMPOSITE = 5 //Composite
        { "STYLE_COMPOSITE", 5L },

        // :L24 - constant long STYLE_RICHTEXT  = 7 //RichText.  *** SEVEN, NOT SIX ***
        { "STYLE_RICHTEXT", 7L },
    };

    [Theory]
    [MemberData(nameof(PresentationStyleCatalogue))]
    public void EachPresentationStyleKeepsItsLegacyNameAndValue(string name, long value)
    {
        FieldInfo? field = typeof(DataWindowServiceBase)
            .GetField(name, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);
        Assert.Equal(typeof(long), field.FieldType);
        Assert.Equal(value, CatalogueValue(field));
    }

    [Fact]
    public void ThePresentationStyleCatalogueSkipsSixAndMustKeepSkippingIt()
    {
        // n_cst_dwsvc.sru:L18-L24 declares 0, 1, 2, 3, 4, 5 and then 7. There is NO constant for 6
        // anywhere in that file, and STYLE_RICHTEXT is 7. The gap is the PowerBuilder presentation-style
        // numbering as PowerBuilder defines it, so closing it would not tidy the set - it would REMAP
        // RICHTEXT, and every stored characterization comparison mentioning richtext would silently
        // start disagreeing. Constraint C-B forbids it: a style read of 6 must remain unmatched here
        // exactly as it is unmatched in the oracle.
        long[] values = [.. DeclaredCatalogue("STYLE_").Select(CatalogueValue).OrderBy(value => value)];

        Assert.Equal(7, values.Length);
        Assert.Equal<long[]>([0L, 1L, 2L, 3L, 4L, 5L, 7L], values);
        Assert.DoesNotContain(6L, values);

        // Stated again against the constant itself, so the spelling and the value are both pinned at
        // compile time and not only through reflection.
        Assert.Equal(7L, DataWindowServiceBase.STYLE_RICHTEXT);
        Assert.NotEqual(6L, DataWindowServiceBase.STYLE_RICHTEXT);

        // A `long` constant set rather than a C# enum, which is what keeps the gap visible at the point
        // of declaration and what stops an out-of-range cast being silently legal.
        Assert.All(DeclaredCatalogue("STYLE_"), field => Assert.Equal(typeof(long), field.FieldType));
    }

    /// <summary>
    /// The column-type categories, transcribed from <c>n_cst_dwsvc.sru:L26-L32</c>.
    /// </summary>
    public static TheoryData<string, long> ColumnTypeCatalogue() => new()
    {
        { "COL_TYPE_UNKNOWN", 0L },   // :L26
        { "COL_TYPE_STRING", 1L },    // :L27
        { "COL_TYPE_INTEGER", 2L },   // :L28
        { "COL_TYPE_DECIMAL", 3L },   // :L29
        { "COL_TYPE_DATETIME", 4L },  // :L30
        { "COL_TYPE_DATE", 5L },      // :L31
        { "COL_TYPE_TIME", 6L },      // :L32
    };

    [Theory]
    [MemberData(nameof(ColumnTypeCatalogue))]
    public void EachColumnTypeCategoryKeepsItsLegacyNameAndValue(string name, long value)
    {
        FieldInfo? field = typeof(DataWindowServiceBase)
            .GetField(name, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);
        Assert.Equal(typeof(long), field.FieldType);
        Assert.Equal(value, CatalogueValue(field));
    }

    [Fact]
    public void TheColumnTypeCatalogueIsContiguousUnlikeThePresentationStyles()
    {
        // Worth asserting explicitly precisely BECAUSE the STYLE_ set immediately above it in the same
        // legacy file does have a gap. The two sets look alike and behave differently, and a reader who
        // learned the gap from STYLE_ could easily "restore" a phantom one here.
        long[] values = [.. DeclaredCatalogue("COL_TYPE_").Select(CatalogueValue).OrderBy(value => value)];

        Assert.Equal(7, values.Length);
        Assert.Equal<long[]>([0L, 1L, 2L, 3L, 4L, 5L, 6L], values);
        Assert.Equal(values.Length - 1, values.Max());
        Assert.Equal(0L, values.Min());
    }

    /// <summary>
    /// Answers to <c>Describe("DataWindow.Processing")</c> and the style each resolves to, which is how
    /// the missing 6 becomes OBSERVABLE rather than merely absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n_cst_dwsvc.sru:L143-L158</c>'s <c>choose case</c> has arms for "1" through "5" and for "7",
    /// and NO ARM FOR "6". Every unrecognised answer - including the Describe sentinels - therefore
    /// falls through to STYLE_DEFAULT.
    /// </para>
    /// <para>
    /// THE EXPECTED VALUES ARE LITERALS AND NOT THE <c>STYLE_*</c> CONSTANTS, WHICH IS DELIBERATE AND
    /// WAS ESTABLISHED BY MUTATION. Written symbolically, this matrix could not detect a renumbering at
    /// all: change <c>STYLE_RICHTEXT</c> to 6 and both the expected value and the switch arm move
    /// together, so every case still passes. Written as literals it fails on the "7" case immediately,
    /// which makes this theory independently load-bearing rather than merely restating
    /// <see cref="ThePresentationStyleCatalogueSkipsSixAndMustKeepSkippingIt"/>. The constant each
    /// literal corresponds to is named in the comment beside it, so readability is not the price.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long> ProcessingTokenToPresentationStyle() => new()
    {
        // :L145-L156 - the six real arms. Literals, per the remarks above.
        { "1", 1L },  // STYLE_GRID
        { "2", 2L },  // STYLE_LABEL
        { "3", 3L },  // STYLE_GRAPH
        { "4", 4L },  // STYLE_CROSSTAB
        { "5", 5L },  // STYLE_COMPOSITE
        { "7", 7L },  // STYLE_RICHTEXT - SEVEN, and this case is what catches a renumbering to 6

        // *** THE GAP, MADE OBSERVABLE. *** There is no arm for "6", so it falls through to the default
        // exactly as an unrecognised token does. Inserting a placeholder constant at 6 - the other
        // tempting way to "tidy" the set - makes this case answer 7 instead of 0 and fails here.
        { "6", 0L },  // STYLE_DEFAULT

        // :L157-L158 - the default arm, which absorbs "0", the two Describe sentinels and the empty
        // answer. Written as a switch on the RAW STRING rather than on a parsed number so a non-numeric
        // answer reaches the default arm by the same route the oracle takes.
        { "0", 0L },     // STYLE_DEFAULT
        { "8", 0L },     // STYLE_DEFAULT
        { "!", 0L },     // STYLE_DEFAULT - the invalid-expression sentinel
        { "?", 0L },     // STYLE_DEFAULT - the undetermined-value sentinel
        { "", 0L },      // STYLE_DEFAULT
        { "grid", 0L },  // STYLE_DEFAULT - a word rather than a token
    };

    [Theory]
    [MemberData(nameof(ProcessingTokenToPresentationStyle))]
    public void ThePresentationStyleResolvesFromTheRawProcessingToken(string processing, long style)
    {
        FakeDataWindowHost host = NewHostWithOneColumn();
        host.Processing = processing;

        ContractProbe probe = new();
        probe.OnInit(host);

        Assert.Equal(style, probe.PresentationStyle());
    }

    /// <summary>
    /// The three published DataWindow buffers and the ordinal <c>common.v1.proto</c> gives each.
    /// </summary>
    /// <remarks>
    /// THE `Primary!` LITERAL PROJECTS ONTO THE PUBLISHED ENUM, AND THE PROJECTION IS THE POINT.
    /// <c>Primary!</c> appears at <c>se_cst_dw.sru:L190</c>, <c>:L220</c> and <c>:L376</c>, and the
    /// contract consumes <c>PowerFramework.Contracts.Common.V1.DwBuffer</c> directly rather than
    /// declaring a parallel enum of its own. Consuming the generated type is what makes the agreement
    /// compiler-enforced rather than merely conventional - but the ORDINALS themselves are still only a
    /// declaration in a <c>.proto</c> file, and nothing in C# checks that they match what the legacy
    /// meant. This theory is the thing holding them.
    /// </remarks>
    public static TheoryData<DwBuffer, int, string> PublishedBufferOrdinals() => new()
    {
        // common.v1.proto - DW_BUFFER_PRIMARY = 0. `Primary!`, the live rows, and also the state of an
        // unassigned `dwbuffer`, which is why it MUST hold ordinal zero.
        { DwBuffer.Primary, 0, "DW_BUFFER_PRIMARY" },

        // common.v1.proto - DW_BUFFER_DELETE = 1. `Delete!`, rows deleted but not yet flushed.
        { DwBuffer.Delete, 1, "DW_BUFFER_DELETE" },

        // common.v1.proto - DW_BUFFER_FILTER = 2. `Filter!`, whose ROW ORDER IS INVERTED relative to the
        // source - the reason n_cst_thread_task_sqlupdate.sru:L237 iterates it backwards.
        { DwBuffer.Filter, 2, "DW_BUFFER_FILTER" },
    };

    [Theory]
    [MemberData(nameof(PublishedBufferOrdinals))]
    public void EachPublishedBufferKeepsTheOrdinalTheProtoDeclares(
        DwBuffer buffer,
        int ordinal,
        string protoName)
    {
        Assert.True(
            (int)buffer == ordinal,
            "shared/PowerFramework.Contracts/Proto/common.v1.proto declares "
            + protoName
            + " = "
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + ", but the generated C# member DwBuffer."
            + buffer.ToString()
            + " has ordinal "
            + ((int)buffer).ToString(CultureInfo.InvariantCulture)
            + ". Renumbering a published enum silently reinterprets every payload already recorded "
            + "against it.");

        Assert.Equal(ordinal, (int)buffer);
    }

    [Fact]
    public void ThePublishedBufferEnumIsConsumedDirectlyRatherThanRedeclared()
    {
        // Exactly three members, in the proto's own order, under the generated C# spellings the whole
        // codebase uses. A fourth would mean the published contract had grown a buffer PowerBuilder does
        // not have.
        Assert.Equal<string[]>(["Primary", "Delete", "Filter"], Enum.GetNames<DwBuffer>());

        // `Primary!` is the DEFAULT, which is what makes an unassigned buffer read as the live rows.
        Assert.Equal(DwBuffer.Primary, default);

        // AND THE COMPILE-TIME HALF OF THE AGREEMENT: the contract's own item-status members are typed
        // with the PUBLISHED enums, not with a parallel local copy, so the two cannot drift.
        MethodInfo getItemStatus = typeof(DataWindowServiceHost)
            .GetMethod(nameof(DataWindowServiceHost.GetItemStatus))!;
        ParameterInfo[] parameters = getItemStatus.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(DwBuffer), parameters[2].ParameterType);
        Assert.Equal(
            "PowerFramework.Contracts.Common.V1.ItemStatus",
            getItemStatus.ReturnType.FullName);

        MethodInfo setItemStatus = typeof(DataWindowServiceHost)
            .GetMethod(nameof(DataWindowServiceHost.SetItemStatus))!;
        Assert.Equal(typeof(DwBuffer), setItemStatus.GetParameters()[2].ParameterType);
    }

    // ==============================================================================================
    //  PHASE 5 - `Describe` AND DATAWINDOW-OBJECT RESOLUTION
    //  --------------------------------------------------------------------------------------------
    //  n_cst_dwsvc.sru:L97 and :L100-L125, the two `_of_getdwobject` accessors:
    //
    //      protected function dwobject _of_getdwobject (readonly long colnum);
    //          return _of_GetDWObject("#" + String(colNum))                       // :L97
    //      end function
    //
    //      protected function dwobject _of_getdwobject (readonly string dwoname);
    //          dwobject dwo                                                       // :L117
    //          if Not IsValidObject(#DataWindow.Object) then return dwo           // :L119
    //          //*当前对象不存在时会异常   "raises when the current object does not exist"  // :L121
    //          dwo = #DataWindow.Object.__Get_Attribute(dwoName,false)            // :L122
    //          return dwo                                                         // :L124
    //      end function
    //
    //  TWO DISTINCT FAILURE MODES, AND KEEPING THEM DISTINCT IS THE BEHAVIOUR. "The DataWindow has no
    //  object model" answers an UNSET HANDLE; "you asked for an object that is not there" RAISES. A port
    //  that collapsed the raise into the null would leave a caller unable to tell the two apart, and the
    //  legacy can.
    // ==============================================================================================

    /// <summary>
    /// Reaches the protected members of <see cref="DataWindowServiceBase"/> that no other double
    /// exposes, so they can be exercised at all.
    /// </summary>
    /// <remarks>
    /// <see cref="FakeDataWindowService"/> already reaches <c>OnEnable</c>, both
    /// <c>GetDataWindowObject</c> overloads and <c>RequireHost</c>, and this suite uses it for those.
    /// <c>GetPresentationStyle</c> and <c>ConvertColumnType</c> are the two remaining protected members
    /// with no accessor anywhere, and a derived type is the only way to reach them; that is the entire
    /// purpose of this probe. It deliberately adds NO behaviour of its own, so nothing asserted through
    /// it can be an artifact of the probe rather than of the contract.
    /// </remarks>
    private sealed class ContractProbe : DataWindowServiceBase
    {
        /// <summary>Reaches <c>GetPresentationStyle</c> [<c>n_cst_dwsvc.sru:L143-L158</c>].</summary>
        public long PresentationStyle() => GetPresentationStyle();

        /// <summary>Reaches <c>ConvertColumnType</c> [<c>n_cst_dwsvc.sru:L503-L517</c>].</summary>
        public static long NormaliseColumnType(string colType) => ConvertColumnType(colType);
    }

    [Fact]
    public void ObjectResolutionByNameAndByColumnNumberReachTheSameHandle()
    {
        (FakeDataWindowHost host, FakeDataWindowService service) = NewAttachedCompanyFixture();
        host.RecordsReads = true;
        host.CallLog.Clear();

        // dw_sqlite.srd:L12 - salary is the fifth column of the primary fixture.
        IDataWindowObject? byName = service.ResolveDataWindowObject("salary");
        IDataWindowObject? byNumber = service.ResolveDataWindowObject(5L);

        Assert.NotNull(byName);
        Assert.NotNull(byNumber);

        // n_cst_dwsvc.sru:L97 is PURE DELEGATION - it composes `"#" + String(colNum)` and calls the
        // by-name accessor - so the two routes must land on the SAME handle rather than merely on equal
        // ones.
        Assert.Same(byName, byNumber);

        Assert.Equal("salary", byName!.Name);
        Assert.Equal("decimal(2)", byName.ColType);
        Assert.Equal("column", byName.Type);
        Assert.Equal(5L, byName.ColumnId());

        // :L122 - both routes go through `Object.__Get_Attribute`, and the positional route proves the
        // "#5" composition at :L97 rather than assuming it.
        Assert.Contains("GetObjectAttribute[\"salary\"]", host.CallLog.Descriptions);
        Assert.Contains("GetObjectAttribute[\"#5\"]", host.CallLog.Descriptions);
    }

    /// <summary>
    /// The six real columns of the primary fixture, by name and by one-based column number.
    /// </summary>
    /// <remarks>
    /// Transcribed from <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>, which declares the
    /// columns in this order and therefore assigns these numbers. The six header text objects declared
    /// at <c>:L15-L20</c> consume NO column number, which is exactly why <c>salary</c> is 5 rather than
    /// 11 - a fixture that numbered every object would silently shift every positional lookup.
    /// </remarks>
    public static TheoryData<string, long, string> PrimaryFixtureColumns() => new()
    {
        { "id", 1L, "number" },       // :L8  - the key AND identity column
        { "name", 2L, "char(100)" },  // :L9
        { "age", 3L, "number" },      // :L10
        { "address", 4L, "char(200)" },  // :L11 - 200 here against 50 in the only DDL; a preserved defect
        { "salary", 5L, "decimal(2)" },  // :L12
        { "birth", 6L, "date" },      // :L13 - `date`, NOT `datetime`; see the coercion near-miss below
    };

    [Theory]
    [MemberData(nameof(PrimaryFixtureColumns))]
    public void EveryPrimaryFixtureColumnResolvesByBothRoutesAndReportsItsDeclaredColumnType(
        string columnName,
        long columnNumber,
        string colType)
    {
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();

        IDataWindowObject? byName = service.ResolveDataWindowObject(columnName);
        IDataWindowObject? byNumber = service.ResolveDataWindowObject(columnNumber);

        Assert.NotNull(byName);
        Assert.Same(byName, byNumber);
        Assert.Equal(columnName, byName!.Name);
        Assert.Equal(columnNumber, byName.ColumnId());
        Assert.Equal(colType, byName.ColType);
    }

    [Fact]
    public void AnAbsentObjectModelAnswersAnUnsetHandleWithoutReachingTheAttributeAccessor()
    {
        // FAILURE MODE ONE [n_cst_dwsvc.sru:L119]:
        //     if Not IsValidObject(#DataWindow.Object) then return dwo
        // The local `dwo` declared at :L117 is never assigned, so an unset handle is returned and NO
        // exception is raised. `IsValidObject` itself is two lines - isvalidobject.srf:L11-L12,
        // `if IsNull(object) then return false` / `return IsValid(object)` - and the null arm is the one
        // this reaches.
        (FakeDataWindowHost host, FakeDataWindowService service) = NewAttachedCompanyFixture();
        host.RecordsReads = true;
        host.ObjectModelValue = null;
        host.CallLog.Clear();

        Assert.False(Predicates.IsValidObject(host.ObjectModel));
        Assert.Null(service.ResolveDataWindowObject("salary"));
        Assert.Null(service.ResolveDataWindowObject(5L));

        // :L119 short-circuits BEFORE :L122, so the attribute accessor is never reached at all - which
        // is what makes the guard meaningful rather than merely defensive.
        Assert.DoesNotContain("GetObjectAttribute", host.CallLog.Members);

        // And restoring a valid object model restores resolution, so the guard is the only thing that
        // was suppressing it.
        host.ObjectModelValue = new object();
        Assert.True(Predicates.IsValidObject(host.ObjectModel));
        Assert.NotNull(service.ResolveDataWindowObject("salary"));
    }

    /// <summary>
    /// Requests for objects the fixture does not declare, in both the by-name and the positional form.
    /// </summary>
    public static TheoryData<string> AbsentObjectNames() => new()
    {
        // A plausible column that simply is not in dw_sqlite.srd.
        "phone",

        // The positional form for a column number past the sixth and last.
        "#7",

        // The positional form for a number no column could carry.
        "#0",

        // A header text object DOES exist by name [dw_sqlite.srd:L15] but carries no column number, so
        // its positional spelling resolves to nothing - the distinction the fixture's numbering rests on.
        "#99",
    };

    [Theory]
    [MemberData(nameof(AbsentObjectNames))]
    public void AnAbsentObjectRaisesRatherThanAnsweringNull(string absentName)
    {
        // FAILURE MODE TWO [n_cst_dwsvc.sru:L121-L122]. The legacy's own comment on :L121 reads
        // 「当前对象不存在时会异常」- "raises an exception when the current object does not exist" - and
        // :L122 performs the lookup anyway. The exception is deliberately allowed to PROPAGATE rather
        // than being folded into the unset handle of failure mode one, because a caller that received
        // null could not tell "this DataWindow has no object model" from "you asked for a column that is
        // not there", and the legacy can.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();

        Assert.Throws<KeyNotFoundException>(() => service.ResolveDataWindowObject(absentName));
    }

    [Fact]
    public void TheTwoResolutionFailureModesRemainDistinguishable()
    {
        // The whole point of the two preceding tests, stated once as a single contrast so the invariant
        // is legible without reading both.
        (FakeDataWindowHost host, FakeDataWindowService service) = NewAttachedCompanyFixture();

        // Object model present, object absent -> RAISES [:L121-L122].
        Assert.Throws<KeyNotFoundException>(() => service.ResolveDataWindowObject("phone"));

        // Object model absent -> unset handle, no raise [:L119]. Note the SAME absent name now answers
        // null, because :L119 short-circuits before the lookup can raise.
        host.ObjectModelValue = null;
        Assert.Null(service.ResolveDataWindowObject("phone"));
    }

    /// <summary>
    /// The <c>ColType</c> reduction table, driven from the primary fixture's six real strings plus the
    /// near-misses the five-character truncation creates.
    /// </summary>
    /// <remarks>
    /// <c>n_cst_dwsvc.sru:L503</c> takes <c>Left(colType,5)</c> and <c>:L504-L517</c> dispatches on the
    /// truncated prefix with ORDINAL comparison throughout. Two consequences drive most of the cases
    /// below. First, the <c>"datet"</c> arm is tested BEFORE the <c>"date"</c> arm [<c>:L510-L513</c>], so
    /// <c>datetime</c> and <c>date</c> reach different arms - a StartsWith-versus-equality confusion
    /// between those two is the single most likely silent defect in the whole table. Second, truncation
    /// makes several plausible database types MISS: <c>numeric</c> truncates to <c>"numer"</c> and not to
    /// <c>"numbe"</c>, and <c>timestamp</c> truncates to <c>"times"</c> and not to <c>"time"</c>. Both
    /// therefore reduce to UNKNOWN, and that is legacy behaviour rather than an oversight.
    /// </remarks>
    public static TheoryData<string, long> ColumnTypeReduction() => new()
    {
        // ---- the six real columns of dw_sqlite.srd:L8-L13 ----
        // :L8 id and :L10 age - "numbe" [:L506].
        { "number", DataWindowServiceBase.COL_TYPE_INTEGER },

        // :L9 name - "char(" [:L504-L505].
        { "char(100)", DataWindowServiceBase.COL_TYPE_STRING },

        // :L11 address - "char(" as well.
        { "char(200)", DataWindowServiceBase.COL_TYPE_STRING },

        // :L12 salary - "decim" [:L508].
        { "decimal(2)", DataWindowServiceBase.COL_TYPE_DECIMAL },

        // :L13 birth - "date" [:L512-L513]. The fixture's birth column is a `date`, so this is the arm
        // the golden master actually exercises.
        { "date", DataWindowServiceBase.COL_TYPE_DATE },

        // ---- THE NEAR-MISS PAIR, which is why the datetime arm is tested first ----
        // "datet" [:L510-L511], reached because Left("datetime",5) is "datet" and not "date".
        { "datetime", DataWindowServiceBase.COL_TYPE_DATETIME },

        // Also "datet", so a longer datetime spelling lands on the same arm.
        { "datetimeoffset", DataWindowServiceBase.COL_TYPE_DATETIME },

        // ---- the remaining declared arms ----
        { "char", DataWindowServiceBase.COL_TYPE_STRING },      // :L504, shorter than five characters
        { "long", DataWindowServiceBase.COL_TYPE_INTEGER },     // :L506
        { "ulong", DataWindowServiceBase.COL_TYPE_INTEGER },    // :L507, exactly five characters
        { "real", DataWindowServiceBase.COL_TYPE_DECIMAL },     // :L509
        { "time", DataWindowServiceBase.COL_TYPE_TIME },        // :L514

        // ---- unmatched, each for a specific and preserved reason [:L516-L517] ----
        // Left("numeric",5) is "numer", which is not "numbe". A real database type that MISSES.
        { "numeric", DataWindowServiceBase.COL_TYPE_UNKNOWN },

        // Left("timestamp",5) is "times", which is not "time". Another real type that MISSES.
        { "timestamp", DataWindowServiceBase.COL_TYPE_UNKNOWN },

        // No arm at all.
        { "blob", DataWindowServiceBase.COL_TYPE_UNKNOWN },

        // The empty answer, which is what an unknown Describe key can yield.
        { "", DataWindowServiceBase.COL_TYPE_UNKNOWN },

        // ORDINAL COMPARISON, so case matters. These are machine-generated tokens and never user text,
        // so the oracle compares them ordinally; a culture-sensitive or case-insensitive compare could
        // match differently under a Turkish-style casing rule.
        { "CHAR(100)", DataWindowServiceBase.COL_TYPE_UNKNOWN },
        { "DATE", DataWindowServiceBase.COL_TYPE_UNKNOWN },
        { "Number", DataWindowServiceBase.COL_TYPE_UNKNOWN },
    };

    [Theory]
    [MemberData(nameof(ColumnTypeReduction))]
    public void TheColumnTypeReductionFollowsTheFiveCharacterPrefixTable(string colType, long category)
    {
        Assert.Equal(category, ContractProbe.NormaliseColumnType(colType));
    }

    [Fact]
    public void TheDatetimeArmIsTestedBeforeTheDateArm()
    {
        // Called out on its own because it is the one ordering in the table that a rewrite would most
        // plausibly break, and because a broken ordering still passes a naive "date maps to DATE" test.
        // n_cst_dwsvc.sru:L510-L511 declares the "datet" arm ABOVE the "date" arm at :L512-L513.
        Assert.Equal(
            DataWindowServiceBase.COL_TYPE_DATETIME,
            ContractProbe.NormaliseColumnType("datetime"));
        Assert.Equal(
            DataWindowServiceBase.COL_TYPE_DATE,
            ContractProbe.NormaliseColumnType("date"));
        Assert.NotEqual(
            ContractProbe.NormaliseColumnType("datetime"),
            ContractProbe.NormaliseColumnType("date"));

        // The mechanism, shown rather than asserted about: five-character truncation is what separates
        // them, and the double publishes the same truncation so the two agree about the near-miss.
        Assert.Equal("datet", FakeColumnType.CoercionPrefix(FakeColumnType.DateTime));
        Assert.Equal("date", FakeColumnType.CoercionPrefix(FakeColumnType.Date));
    }

    [Fact]
    public void EveryPrimaryFixtureColumnReducesThroughItsOwnResolvedHandle()
    {
        // The theory above drives the reduction from LITERALS. This drives it end to end instead: resolve
        // each column through `Object.__Get_Attribute`, read the `ColType` the handle reports, and reduce
        // that. It is the composition the ported service layer actually performs, so it catches a
        // fixture and a reduction that are each individually right but disagree with one another.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();

        (string Column, long Category)[] expected =
        [
            ("id", DataWindowServiceBase.COL_TYPE_INTEGER),       // dw_sqlite.srd:L8
            ("name", DataWindowServiceBase.COL_TYPE_STRING),      // :L9
            ("age", DataWindowServiceBase.COL_TYPE_INTEGER),      // :L10
            ("address", DataWindowServiceBase.COL_TYPE_STRING),   // :L11
            ("salary", DataWindowServiceBase.COL_TYPE_DECIMAL),   // :L12
            ("birth", DataWindowServiceBase.COL_TYPE_DATE),       // :L13
        ];

        Assert.All(
            expected,
            column =>
            {
                IDataWindowObject? handle = service.ResolveDataWindowObject(column.Column);
                Assert.NotNull(handle);
                Assert.Equal(column.Category, ContractProbe.NormaliseColumnType(handle!.ColType));
            });
    }

    /// <summary>
    /// Property expressions that name nothing the fixture declares, every one of which answers the
    /// invalid-expression sentinel rather than raising.
    /// </summary>
    /// <remarks>
    /// THE SENTINELS ARE NOT NORMALISED, AND THAT IS LOAD BEARING. <c>"!"</c> (invalid expression),
    /// <c>"?"</c> (value cannot be determined) and <c>""</c> are THREE DISTINCT OUTCOMES that ported
    /// callers branch on individually at <c>n_cst_dwsvc.sru:L587</c>, <c>:L590</c>, <c>:L598</c> and
    /// <c>:L599</c>. A host that raised on an unknown key would make every one of those branches
    /// unreachable, and a host that collapsed the three into one would make them indistinguishable.
    /// </remarks>
    public static TheoryData<string> UnrecognisedPropertyExpressions() => new()
    {
        // A DataWindow-level key that is not one of the well-known ones.
        "DataWindow.NoSuchProperty",

        // A group-band probe against a fixture that declares no group bands, which is exactly what makes
        // `Describe("DataWindow.Header.1.Height") <> "!"` [n_cst_dwsvc.sru:L850] answer false.
        "DataWindow.Header.1.Height",

        // A known object with a property it does not recognise.
        "salary.NoSuchProperty",

        // An unknown object with a well-known property.
        "phone.ColType",

        // No separator at all, so nothing names an object.
        "salary",

        // A trailing separator, so the property half is empty.
        "salary.",

        // A leading separator, so the object half is empty.
        ".ColType",
    };

    [Theory]
    [MemberData(nameof(UnrecognisedPropertyExpressions))]
    public void AnUnrecognisedPropertyExpressionAnswersTheSentinelRatherThanRaising(string property)
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateCompanyFixture();

        string answer = host.Describe(property);

        Assert.Equal(FakeDataWindowHost.InvalidExpressionSentinel, answer);
        Assert.Equal("!", answer);
    }

    [Fact]
    public void TheThreeDescribeOutcomesRemainThreeDistinctValues()
    {
        // n_cst_dwsvc.sru:L587, :L590, :L598 and :L599 each branch on ONE of these, so collapsing any
        // two would silently merge two branches.
        Assert.Equal("!", FakeDataWindowHost.InvalidExpressionSentinel);
        Assert.Equal("?", FakeDataWindowHost.UndeterminedValueSentinel);
        Assert.NotEqual(
            FakeDataWindowHost.InvalidExpressionSentinel,
            FakeDataWindowHost.UndeterminedValueSentinel);
        // Argument order is dictated by xUnit2000, which requires the constant on the `expected` side;
        // for a NotEqual the direction carries no meaning either way.
        Assert.NotEqual(FakeDataWindowHost.InvalidExpressionSentinel, string.Empty);
        Assert.NotEqual(FakeDataWindowHost.UndeterminedValueSentinel, string.Empty);
    }

    [Fact]
    public void ARecognisedPropertyExpressionStillAnswersTheRealValue()
    {
        // The sentinel theory would pass vacuously against a host that answered "!" to EVERYTHING, so
        // this fact establishes that recognised keys do resolve. Each value is transcribed from
        // dw_sqlite.srd with its locator.
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateCompanyFixture();

        Assert.Equal("decimal(2)", host.Describe("salary.ColType"));  // :L12
        Assert.Equal("column", host.Describe("salary.Type"));         // :L12, the object KIND
        Assert.Equal("1", host.Describe("DataWindow.Processing"));    // :L3, processing=1 is GRID
        Assert.Equal("COMPANY", host.Describe("DataWindow.Table.UpdateTable"));   // :L14
        Assert.Equal("1", host.Describe("DataWindow.Table.UpdateWhere"));         // :L14 updatewhere=1
        Assert.Equal("no", host.Describe("DataWindow.Table.UpdateKeyInPlace"));   // :L14
        Assert.Equal("6", host.Describe("DataWindow.Column.Count"));  // :L8-L13, six data columns

        // The sort's TRAILING SPACE is part of the literal at :L14 and is carried verbatim, because
        // trimming it would be exactly the silent correction constraint C-B forbids.
        Assert.Equal("age A salary A ", host.Describe("DataWindow.Table.Sort"));
    }

    [Fact]
    public void DescribeIsTheDominantMemberAndIsDeclaredAbstractOverAPlainStringKey()
    {
        // 37 measured call sites - 31 in n_cst_dwsvc.sru and 6 in se_cst_dw.sru - which is an order of
        // magnitude more than any other member on the contract: every property read in the service layer
        // funnels through it. It is ABSTRACT because there is no host-independent answer to give, and it
        // takes and returns a plain non-nullable string because the legacy's three outcomes are all
        // strings and none of them is null.
        MethodInfo describe = typeof(DataWindowServiceHost)
            .GetMethod(nameof(DataWindowServiceHost.Describe))!;

        Assert.True(describe.IsAbstract);
        Assert.True(describe.IsPublic);
        Assert.Equal(typeof(string), describe.ReturnType);

        ParameterInfo[] parameters = describe.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.False(parameters[0].ParameterType.IsByRef);
    }

    /// <summary>
    /// Every shape a <c>dwo.ID</c> can arrive in, named by a discriminator, and the column number each
    /// normalises to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>dwo.ID</c> IS AN <c>any</c> IN THE LEGACY, read at twelve sites in <c>se_cst_dw.sru</c>, so
    /// the contract types it <c>object?</c> and the extension does the narrowing. Every arm below is a
    /// real arrival shape rather than a hypothetical one: a PowerBuilder <c>any</c> holding a column
    /// identifier can present as any numeric width, as text, or as null.
    /// </para>
    /// <para>
    /// TWO ANSWERS CARRY THE MOST WEIGHT, AND THEY ARE THE TWO THAT ARE NOT NUMBERS. A NULL identifier
    /// answers <see langword="null"/> - "there is no column here" - while an UNPARSEABLE one answers
    /// <c>0</c>, which is what the legacy's own <c>Long()</c> conversion yields and which no real column
    /// carries. Collapsing null to 0 would convert "no column" into "column zero", and AAP 0.4.5.4
    /// forbids that in terms.
    /// </para>
    /// <para>
    /// THE DISCRIMINATOR IS A STRING RATHER THAN THE BOXED VALUE ITSELF, because a
    /// <c>TheoryData&lt;object?, long?&gt;</c> declares a type argument xunit cannot prove serializable
    /// and would raise a diagnostic that <c>TreatWarningsAsErrors</c> turns into a build failure.
    /// <see cref="BoxIdentifier(string)"/> maps each discriminator to its value and THROWS on an
    /// unknown one, so a typo in this matrix fails loudly instead of silently testing null.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long?> ColumnIdentifierShapes() => new()
    {
        // The null arm. NEVER 0 - see the remarks.
        { "null", null },

        // The integral widths, every one of which converts losslessly.
        { "long", 5L },
        { "int", 5L },
        { "short", 5L },
        { "byte", 5L },
        { "sbyte", 5L },
        { "uint", 5L },
        { "ushort", 5L },
        { "ulong", 5L },

        // An unsigned value too large for a signed 64-bit column number SATURATES rather than wrapping.
        // Wrapping would produce a negative column number, which resolves to nothing and would look
        // like a missing column rather than an out-of-range identifier.
        { "ulong-overflow", long.MaxValue },

        // The fractional widths TRUNCATE TOWARD ZERO, which is what PowerScript's own `Long()`
        // conversion does - it does not round.
        { "decimal", 5L },
        { "decimal-truncate", 5L },
        { "double", 5L },
        { "double-truncate", 5L },
        { "float", 5L },

        // The fractional extremes saturate for the same reason the unsigned one does.
        { "decimal-max", long.MaxValue },
        { "decimal-min", long.MinValue },
        { "double-max", long.MaxValue },
        { "double-min", long.MinValue },

        // NaN is not a number and cannot truncate, so it answers the unparseable 0 rather than raising.
        { "double-nan", 0L },

        // Text, parsed invariant-culture because PowerScript's `String(long)` composes digits with no
        // group separator and a culture that added one would resolve nothing.
        { "string-numeric", 5L },
        { "string-invalid", 0L },
        { "string-empty", 0L },

        // Anything else reaches the default arm and answers the unparseable 0.
        { "bool", 0L },
    };

    /// <summary>
    /// Maps an identifier-shape discriminator onto the boxed value it names.
    /// </summary>
    /// <param name="shape">A discriminator from <see cref="ColumnIdentifierShapes"/>.</param>
    /// <returns>The boxed identifier, which may be <see langword="null"/> for the null arm.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The discriminator is not one this method knows. Failing loudly is the point: a silent fallback
    /// would let a mistyped matrix row test the null arm while appearing to test something else.
    /// </exception>
    private static object? BoxIdentifier(string shape) => shape switch
    {
        "null" => null,
        "long" => 5L,
        "int" => 5,
        "short" => (short)5,
        "byte" => (byte)5,
        "sbyte" => (sbyte)5,
        "uint" => 5U,
        "ushort" => (ushort)5,
        "ulong" => 5UL,
        "ulong-overflow" => ulong.MaxValue,
        "decimal" => 5m,
        "decimal-truncate" => 5.9m,
        "decimal-max" => decimal.MaxValue,
        "decimal-min" => decimal.MinValue,
        "double" => 5d,
        "double-truncate" => 5.9d,
        "double-nan" => double.NaN,
        "double-max" => double.MaxValue,
        "double-min" => double.MinValue,
        "float" => 5f,
        "string-numeric" => "5",
        "string-invalid" => "not-a-number",
        "string-empty" => "",
        "bool" => true,
        _ => throw new ArgumentOutOfRangeException(
            nameof(shape),
            shape,
            "ColumnIdentifierShapes names an identifier shape BoxIdentifier does not know."),
    };

    [Theory]
    [MemberData(nameof(ColumnIdentifierShapes))]
    public void TheColumnIdExtensionNormalisesEveryShapeAnIdentifierCanArriveIn(
        string shape,
        long? columnNumber)
    {
        Assert.Equal(columnNumber, DataWindowObjectExtensions.ToColumnId(BoxIdentifier(shape)));
    }

    [Fact]
    public void TheInstanceColumnIdFormAgreesWithTheFixtureAndRefusesAMissingHandle()
    {
        // The instance form is the one ported call sites use, so it is asserted against real handles
        // rather than against boxed literals. Column numbers come from dw_sqlite.srd:L8-L13.
        (_, FakeDataWindowService service) = NewAttachedCompanyFixture();

        Assert.Equal(1L, service.ResolveDataWindowObject("id")!.ColumnId());
        Assert.Equal(6L, service.ResolveDataWindowObject("birth")!.ColumnId());

        // A MISSING HANDLE RAISES rather than answering a plausible zero. Zero is a legitimate answer for
        // an unparseable identifier, so returning it for a null handle as well would make the two
        // indistinguishable - the same reasoning that keeps the two resolution failure modes apart.
        Assert.Throws<ArgumentNullException>(() => DataWindowObjectExtensions.ColumnId(null!));

        // And a handle whose identifier is null answers null rather than raising, because "this object
        // carries no column number" is an ordinary state for a text object or a computed field.
        FakeDataWindowObject withoutId = new("compute_1", colType: string.Empty, id: null);
        Assert.Null(withoutId.ColumnId());
    }
}
