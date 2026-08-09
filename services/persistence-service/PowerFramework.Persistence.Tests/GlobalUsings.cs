// ==================================================================================================
//  GlobalUsings.cs - PowerFramework.Persistence.Tests
//
//  THE SINGLE OWNER OF THIS ASSEMBLY'S `global using` DIRECTIVES.
//  The sibling PowerFramework.Persistence.Tests.csproj says so explicitly, under its heading
//  "NO GLOBAL USING ITEMS HERE": it deliberately declares no MSBuild <Using> item, so that this
//  one concern lives in exactly one place, discoverable from the file name. The two mechanisms are
//  interchangeable to the compiler - Roslyn silently deduplicates an identical global using
//  arriving from both - but splitting the concern across both is how a reader loses track of which
//  namespaces are ambient. If a namespace is ambient in this assembly, it is because of this file.
//
//  For contrast, and because the four service test projects are otherwise deliberately identical:
//  the Security test project takes the other route with <Using Include="Xunit" /> in its project
//  file, and the Gateway test project carries no GlobalUsings.cs at all. That divergence is
//  pre-existing and is not this file's to reconcile.
//
//  WHY THIS FILE IS NECESSARY AT ALL
//  The repository-root Directory.Build.props enables ImplicitUsings for every project,
//  applications and test projects alike. That supplies the BCL set for Microsoft.NET.Sdk -
//  System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Threading
//  and System.Threading.Tasks - and NOTHING ELSE. In particular it does not supply Xunit, and
//  xunit.v3 ships no implicit using of its own. Without section 4 below, every sibling test class
//  would fail with CS0246 on Fact, Theory, MemberData and Assert unless it repeated the import
//  itself.
//
//  WHY THE DIRECTIVES ARE IN THIS ORDER, WHICH IS NOT THE ORDER YOU WOULD CHOOSE FOR READING
//  Strict ordinal order, aliases last. That is not a preference and it must not be "tidied" into
//  a more logical narrative order - the repository-root .editorconfig sets
//  dotnet_sort_system_directives_first = true with dotnet_separate_import_directive_groups = false,
//  so the IMPORTS formatter requires exactly this sequence and rejects any other with
//  `error IMPORTS: Fix imports ordering`. It also strips blank lines between adjacent import
//  groups, which is why each section's banner sits flush against the directive above it rather
//  than being separated by whitespace. The section numbering below therefore follows the ordering
//  the tooling imposes; the sections are numbered so the cross-references stay readable, not
//  because that is the order in which they matter. Xunit sorting last, after every
//  PowerFramework.* namespace, is simply where "X" falls.
//
//  HOW THIS FILE INTERACTS WITH SIBLINGS THAT STILL DECLARE THEIR OWN `using` DIRECTIVES
//  Several siblings do, and that is not a defect and not a duplication to clean up. Two verified
//  facts govern it, both established by building this service rather than assumed:
//
//    1. A file-local `using X;` that duplicates a `global using X;` produces ZERO diagnostics here.
//       The unused-import diagnostics of the CS8019 / IDE0005 family are hidden by default, and
//       neither EnforceCodeStyleInBuild nor GenerateDocumentationFile is set at the repository
//       root, so nothing promotes them. TreatWarningsAsErrors is inherited as true and is
//       therefore harmless in this specific respect.
//
//    2. File-level using directives are an INNER scope, consulted BEFORE the global usings
//       declared here. A file that imports a namespace itself resolves simple names through its
//       own import list and never reaches the global scope, so it can never be destabilised by a
//       name that is ambiguous among the globals below. This is precisely what makes the alias
//       section at the end of this file safe to add to an assembly whose existing files already
//       compile.
//
//  WHAT IS DELIBERATELY *NOT* DECLARED HERE, AND WHY
//    * No System.Reflection and no System.Globalization. Both are genuinely used by some siblings
//      (reflection to assert the shape of the preserved constant catalogues, globalization to pin
//      culture-sensitive formatting) but only by some. Making a hundred-odd reflection type names
//      ambient across every test file in order to spare three of them one line each is a poor
//      trade: it widens the collision surface for no benefit and makes reflection-based assertions
//      read as the norm when they are the exception. Those files import them locally.
//    * No namespace belonging to PowerFramework.Gateway, PowerFramework.DataServices or
//      PowerFramework.Security (constraint C-A, decompose rather than collapse). This project
//      holds exactly one ProjectReference - its own application project - so no such namespace is
//      even reachable; a directive naming one would not compile. Cross-service coupling is
//      confined to the published contracts project, and that is a boundary definition rather than
//      a shared-code back door.
//    * No namespace belonging to any deferred capability area - DesignSystem, Documents,
//      Integration or ScriptBridge (constraint C-D). None exists, and inventing a speculative
//      import for one would be a stub of a target this phase forbids implementing.
//
//  RULES POSITION
//  No user rules were provided for this project: the rules document contains exactly one line
//  saying so, and re-reading it returns the same. Nothing is invented or back-filled from
//  convention in their place. The binding constraints are therefore the enterprise-standard
//  baseline - warnings as errors in test code too, no secret in source, no unused coupling - plus
//  the named non-rule constraints C-A, C-B, C-D and C-K, each cited above and below at the point
//  it applies, as C-K requires.
//
//  LEGACY PROVENANCE
//  This file ports no behaviour, so it has no legacy counterpart to translate: PowerBuilder has no
//  namespaces and no import statements at all, resolving one flat global namespace by the ordering
//  of the target's library list. The behavioural oracle for the sibling tests is the read-only
//  legacy tree - principally ws_objects/pfw.shared.pbl.src/retcode.sru for the return-code algebra
//  and ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw for the sole DDL and the SQLite URI grammar.
//  Nothing here reads, references or depends on it.
// ==================================================================================================

// --------------------------------------------------------------------------------------------------
//  1. THE PUBLISHED CONTRACTS
//     The generated stubs, whose C# namespaces come from the csharp_namespace option in each
//     protocol definition rather than from any folder name:
//       Proto/common.v1.proto      -> PowerFramework.Contracts.Common.V1       (20 types)
//       Proto/persistence.v1.proto -> PowerFramework.Contracts.Persistence.V1  (94 types)
//
//     Common.V1 is consumed today by DbErrorDataTests (DbError), ItemStatusTests and
//     ItemStatusMachineBufferScreenTests (the ItemStatus and DwBuffer enums).
//
//     Persistence.V1 is consumed by no sibling YET, and is declared here anyway - the one place
//     this file departs from "declare only what is already consumed". Three reasons, recorded so
//     the departure is auditable rather than accidental (C-K). It is required by this file's own
//     specification; it is the namespace carrying contracts C-05 Query, C-06 Update, C-07 Command
//     and C-08 Transaction, so the planned QueryServiceTests, UpdateServiceTests and
//     CommandAndTransactionServiceTests cannot be written without it; and an unused global using
//     provably costs nothing here - verified by building one, which produced zero warnings and
//     zero errors, because the unused-import diagnostics are hidden by default in this
//     configuration. The alternative, letting every gRPC-facing test file import it locally, is
//     exactly the repetition this file exists to remove.
// --------------------------------------------------------------------------------------------------
global using PowerFramework.Contracts.Common.V1;
global using PowerFramework.Contracts.Persistence.V1;
// --------------------------------------------------------------------------------------------------
//  2. THE SUBJECT UNDER TEST
//     Exactly the four namespaces the PowerFramework.Persistence application project actually
//     declares today, each verified against the real folder and each genuinely consumed by at
//     least one sibling:
//
//       Buffers   ItemStatusMachine, ItemStatusExtensions  <- ItemStatusTests,
//                                                             ItemStatusMachineBufferScreenTests
//       Data      CompanyEntity                            <- CompanyEntityTests
//       Errors    DbErrorData, DbErrorMessages             <- DbErrorDataTests
//       Sql       SelectStatementModel                     <- SelectStatementModelTests,
//                                                             SelectStatementModelCompoundSelectTests
//
//     Internal types among these are reachable because the application project grants
//     InternalsVisibleTo("PowerFramework.Persistence.Tests"); SelectStatementModel,
//     ItemStatusMachine and ItemStatusExtensions are all internal and all driven from here.
//
//     SEVEN FURTHER NAMESPACES ARE DELIBERATELY ABSENT, AND THIS IS THE MOST IMPORTANT MAINTENANCE
//     NOTE IN THE FILE. Sql.Paging, Concurrency, Tasks, Tasks.TaskProxies, Transactions,
//     Configuration and Grpc are all planned for this service and none of them exists yet. A
//     global using naming a namespace that does not exist is not a harmless forward declaration -
//     it is an immediate hard failure. Verified by building it: adding
//     PowerFramework.Persistence.Sql.Paging here produces
//       error CS0234: The type or namespace name 'Paging' does not exist in the namespace
//                     'PowerFramework.Persistence.Sql'
//     and takes the whole service's build down with it, this file being compiled into every
//     compilation unit. Each of the seven is therefore added HERE, to this section, only once the
//     corresponding application folder genuinely declares it - never in anticipation.
//
//     Adding one is also not automatic even then: Grpc in particular will introduce QueryService,
//     UpdateService, CommandService and TransactionService, which are the same simple names as the
//     generated service classes imported in section 1. Whoever adds it resolves that the way
//     section 5 resolves RetCode - with an alias, never by dropping a namespace or renaming
//     anything in the application project.
// --------------------------------------------------------------------------------------------------
global using PowerFramework.Persistence.Buffers;
global using PowerFramework.Persistence.Data;
global using PowerFramework.Persistence.Errors;
global using PowerFramework.Persistence.Sql;
// --------------------------------------------------------------------------------------------------
//  3. THE SHARED KERNEL
//     Nine types, of which the tests lean on five: RetCode (the ported return-code algebra),
//     Enums (SQL_MS_REPLACE / SQL_MS_APPEND / SQL_MS_PREPEND and the database-type constants the
//     paging rewriters dispatch on), Predicates (the tri-state boundary semantics, where a
//     prevention still reads as a success and cancelled is neither succeeded nor failed), Bits
//     (MakeLong, which reproduces the legacy packing of retrieval progress into two 16-bit words)
//     and Formatting. Currently consumed by SelectStatementModelTests and
//     SelectStatementModelCompoundSelectTests via Enums.SQL_MS_*.
//
//     It arrives transitively through the application project's own ProjectReference; this test
//     project references nothing but the application project, which is what keeps constraint C-I -
//     build and test independently from a clean checkout - mechanically true.
// --------------------------------------------------------------------------------------------------
global using PowerFramework.Shared.Kernel;
// --------------------------------------------------------------------------------------------------
//  4. THE TEST FRAMEWORK
//     Load-bearing and non-negotiable: this is the directive the whole file exists for. Supplies
//     Fact, Theory, InlineData, MemberData, ClassData, IClassFixture, Assert and the Skip helpers
//     to every sibling. Note that xunit.v3 has no Xunit.Abstractions namespace - the v2 home of
//     ITestOutputHelper - so a file needing test output takes it from Xunit directly.
//
//     It sits fourth only because "X" sorts after "P"; see the ordering note in the header.
// --------------------------------------------------------------------------------------------------
global using Xunit;
// --------------------------------------------------------------------------------------------------
//  5. AMBIGUITY RESOLUTION
//
//     A reflection sweep over all seven namespaces above plus Xunit - 168 type names in total -
//     found EXACTLY ONE simple name declared in more than one of them:
//
//       RetCode  ->  PowerFramework.Shared.Kernel  ||  PowerFramework.Contracts.Common.V1
//
//     Verified as a real failure, not a theoretical one. A probe file with no local usings, relying
//     purely on the globals above, produces:
//       error CS0104: 'RetCode' is an ambiguous reference between
//                     'PowerFramework.Shared.Kernel.RetCode' and
//                     'PowerFramework.Contracts.Common.V1.RetCode'
//     The existing siblings are unaffected only because each imports what it needs locally, per
//     fact 2 of the header. Every future sibling that relies on these globals alone would hit it.
//
//     Worth stating what did NOT collide, because it confirms two deliberate upstream decisions
//     rather than luck. ItemStatus is clean: PowerFramework.Persistence.Buffers declares
//     ItemStatusMachine and ItemStatusExtensions and pointedly does NOT declare a type named
//     ItemStatus, consuming the generated Common.V1 enum instead. DwBuffer is clean for the same
//     reason - it exists once, in Common.V1. Neither needs an alias, and adding one would be
//     noise. The notify-code enums that preserve the legacy numeric collision at value 1 - the
//     query proxy's max-rows code against the update proxy's progress code - belong to
//     Tasks.TaskProxies, which section 2 explains does not exist yet; they are two separate types
//     by design and whoever imports that namespace must alias them rather than merge them.
//
//     THE TWO ALIASES BELOW RENAME REFERENCES. THEY DO NOT COLLAPSE TYPES (constraint C-B).
//     Both RetCodes remain distinct, both remain reachable, and neither is redefined. That
//     matters here more than it usually would, because the two types are genuinely different
//     things that merely share a name:
//
//       PowerFramework.Shared.Kernel.RetCode
//           The in-process constant catalogue ported from ws_objects/pfw.shared.pbl.src/retcode.sru,
//           preserving the legacy SCREAMING_SNAKE spellings verbatim - OK, PREVENT, FAILED,
//           CANCELED, E_INVALID_ARGUMENT - because those identifiers appear in serialized
//           payloads, log records and characterization recordings where a rename would silently
//           invalidate every stored comparison.
//
//       PowerFramework.Contracts.Common.V1.RetCode
//           A generated protobuf wrapper message that is deliberately EMPTY OF FIELDS and, as its
//           own protocol definition states, is never sent as a message. It exists only to scope
//           the nested enum. protoc also PascalCases that enum's members - Ok, Prevent, Failed,
//           Canceled, EInvalidArgument - so the two types do not even spell their members alike.
//
//     Direction of the binding. The bare name goes to the kernel catalogue, because that is how
//     the migration plan writes these constants throughout (RetCode.OK,
//     RetCode.E_INVALID_ARGUMENT), because this project's subject under test is Persistence
//     behaviour expressed against that algebra, and because a using-alias directive is resolved
//     ahead of the namespace imports at the same scope, which is what makes it a fix for CS0104
//     rather than another candidate for it.
//
//     This is a considered divergence from the sibling shared/PowerFramework.Contracts.Tests,
//     which binds the bare name to the generated message and aliases the other as KernelRetCode.
//     That is right there and wrong here: that project's subject under test IS the wire contract.
//     The alias is file-local in that project, so the two conventions cannot interfere.
// --------------------------------------------------------------------------------------------------

// The in-process return-code algebra. Resolves the CS0104 above.
global using RetCode = PowerFramework.Shared.Kernel.RetCode;

// The generated wire enum, kept reachable under a name of its own so that binding the bare name
// above narrows nothing. This is the alias that carries real weight for the gRPC-facing tests:
// common.v1.RetCode.Value appears as a field type twenty times across persistence.v1.proto -
// OperationStatus.ret_code among them - so assertions read WireRetCode.Ok rather than a
// fully-qualified PowerFramework.Contracts.Common.V1.RetCode.Types.Value.Ok. The enum is aliased
// rather than its wrapper message because the wrapper is never sent and only the enum is used.
global using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
