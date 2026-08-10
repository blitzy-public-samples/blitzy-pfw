# ==================================================================================================
#  services/dataservices-service/Dockerfile - THE CONTAINER IMAGE FOR THE DATASERVICES SERVICE
#  ------------------------------------------------------------------------------------------------
#  ONE IMAGE, FOR ONE SERVICE (constraint C-J). DataServices owns the DataWindow retrieval /
#  validation / update triple, the 22-event DataWindow chain and the column-expression engine, ported
#  from the 13 objects of the legacy DataWindow service library. This file produces that service's
#  image and nothing else: two stages, exactly one final image, no variant tag and no second final
#  stage. Each of the three sibling services carries its own Dockerfile; none of them is copied,
#  restored or built here. DataServices reaches Persistence and Security over the network through the
#  published contracts, never through anything baked into this image - so instance counts can vary
#  per service, which is the independently-scalable half of C-J.
#
#  EVERY DECISION BELOW IS ANNOTATED WITH THE CONSTRAINT IT SERVES (constraint C-K). C-K asks that
#  every technology-specific and boundary-specific decision be documented, including the rejected
#  alternatives and the reason each was rejected; the commentary density in this file IS that
#  discharge, not decoration. Where a choice was measured rather than reasoned about, the measurement
#  is quoted. Do not compress these notes away.
#
#  LEGACY REFERENCE - READ FOR BUILD INTENT ONLY, NEVER TRANSLATED
#  The two PowerBuilder project objects both declare `EXE:pfw.exe`: ONE fused executable linking 28
#  or 29 compiled libraries into a single process, loaded into one flat global namespace. That is the
#  artifact this decomposition replaces, and it is the whole reason there is one image per service
#  rather than one image for the estate. Neither object is translated and neither could be: they
#  disagree on library count (project.srj declares 29 PBD entries including pfw.utility.sqlite.pbl,
#  p_pfw.srj declares 28 and omits it) and on vendor (`COM:Appeon` versus `COM:Sybase, Inc.`), one
#  embeds developer-workstation absolute paths (`OBJ:F:\pfw\...`) together with the retired
#  pfwUtils.pbl, and BOTH name pfw.utility.imgcodec.pbl, which exists nowhere in this repository - so
#  neither would build as written. No library list, version or vendor string below derives from
#  either of them.
#
#  NOTHING FROM THE READ-ONLY LEGACY TREE ENTERS THIS IMAGE (constraint C-C)
#  No COPY or ADD instruction below names ws_objects/, a *.pbl, *.pbt, *.pbw, *.pbr, *.pbd or *.pbg
#  file, oldversion/, pack/, res/, samples/, sciter_control/, tests/blink|sciter|webview/, or any of
#  the seven root native binaries (pfw.dll, pfwx.dll, blink.dll, blinkfast.dll, sciter.dll,
#  sqlite3.dll, sqlite3.cipher.dll). The repository-root .dockerignore excludes all of them from the
#  build context AND does so partly as a secrets control, since six of the eight in-source credential
#  sites recorded in docs/SECRETS.md live in those regions. Every COPY below nevertheless names an
#  allowed path EXPLICITLY rather than leaning on a blanket `COPY . .`, so a single regression in
#  that ignore file cannot silently pull closed-source binaries or live credentials into a layer, and
#  the contents of this image are legible and auditable from this file alone.
#
#  THE ONE APPARENT EXCEPTION, AND WHY IT IS NOT ONE. pfw.i18n.xml is copied below. It is a read-only
#  legacy DATA input (AAP 0.2.1.2), not part of the read-only legacy code boundary (AAP 0.2.2.1), and
#  it is a genuine BUILD INPUT - see the note on the root-inputs COPY. It is only ever read and
#  copied; nothing here edits, moves, renames or re-encodes it.
#
#  THE BUILD CONTEXT IS THE REPOSITORY ROOT, NOT THIS DIRECTORY (constraints C-J and C-I)
#  Build it as:  docker build -f services/dataservices-service/Dockerfile .   (from the repo root)
#  Compose declares it as:  context: ..   dockerfile: services/dataservices-service/Dockerfile
#
#  C-I requires this service to build and test independently from a clean checkout, and the root
#  context is what makes that true for the IMAGE as well as for the mandated per-service dotnet
#  command: everything the build needs is reachable, and nothing belonging to another service is.
#
#  This is structural, not stylistic, and it is why the paths below look unusual for a Dockerfile
#  that lives two directories deep. PowerFramework.DataServices.csproj carries six ProjectReference
#  entries that rise three levels into shared/ - PowerFramework.Contracts plus Shared.Kernel,
#  Shared.Eventful, Shared.Localization, Shared.Containers and Shared.Diagnostics - and MSBuild
#  resolves Directory.Build.props, Directory.Packages.props and global.json by walking UP from each
#  project to the repository root. A Docker build cannot reach outside its own context, so a context
#  scoped to services/dataservices-service/ can see none of that and restore fails. Narrowing the
#  context looks tidier and does not work.
#
#  EVERY SOURCE PATH IN EVERY COPY BELOW IS THEREFORE REPOSITORY-ROOT-RELATIVE, and the destination
#  path repeats the same repository-relative structure under /src so that MSBuild's directory walk-up
#  finds the root property sheets exactly where it expects them. Flattening the layout would lose
#  them silently and restore would fail on the versionless package references.
#
#  WHAT IS DELIBERATELY ABSENT FROM THIS FILE
#    * No sibling service. Nothing under services/gateway-service/, services/persistence-service/ or
#      services/security-service/ is copied, restored, built or referenced (constraint C-A). This
#      image must build with no other service present, and it does.
#    * No test project. PowerFramework.DataServices.Tests produces this service's
#      coverage.cobertura.xml and it runs in CI, not in the shipped image. dataservices-service.slnx
#      enumerates it alongside the application, which is exactly why the build below targets the
#      APPLICATION .csproj directly rather than the solution: restoring or publishing the solution
#      would drag the test project, xunit.v3, the test SDK and the coverage collector in for nothing.
#    * No root PowerFramework.slnx. Building it here would pull all twenty projects, including the
#      three sibling services, into this image's build and forfeit the independence C-A requires.
#    * No .slnf solution filter, anywhere, ever. Legacy solution filters are incompatible with the
#      .slnx XML solution format and fail with MSB4014 during restore, build and test.
#    * No NuGet.config. The repository has none; naming a path that does not exist fails the COPY.
#    * No docs/, .github/, orchestration/, tests/e2e/ or characterization/ path. All are excluded
#      from the build context, so referencing any of them would break the build.
#    * No secret, in any form (constraint C-F). No ENV and no ARG below carries a key, token,
#      password, certificate, passphrase or connection credential, and no such value may ever be
#      added. SECURITY_JWT_SIGNING_KEY is the system's single signing secret and belongs to the
#      Security service alone; DataServices holds VERIFICATION material only (constraint C-G), so no
#      signing key of any spelling appears here. The server certificate this service's TLS listener
#      requires, and the client-certificate pair it may present to Security, are MOUNTED read-only
#      from the orchestration secret layer at run time and reach the application through the options
#      pattern - see the TLS note in the runtime stage.
#    * Nothing for a deferred capability (constraint C-D). DesignSystem, Documents, Integration and
#      ScriptBridge have no stage, layer, label, port, environment variable or build input here.
#    * No second port. This service is gRPC-primary with a thin REST projection consumed only by
#      Gateway, and ASP.NET Core carries both on the single port 5102. Port 5103 is the deliberately
#      reserved Phase-2 DesignSystem slot and is not touched.
#    * No extra tooling. No shell utility, package, debugging agent or telemetry sidecar is installed
#      into either stage; the readiness probe uses only what the runtime image already ships.
#    * No performance, latency, throughput or availability claim, and no trimming, single-file,
#      ReadyToRun, AOT, tiered-compilation or garbage-collector setting. The repository publishes no
#      such budget, so none is asserted here or used to justify any choice below (constraint C-B).
# ==================================================================================================


# --------------------------------------------------------------------------------------------------
# STAGE 1 - BUILD
#
# mcr.microsoft.com/dotnet/sdk:10.0 is matched to global.json, which pins SDK 10.0.302 with
# rollForward: latestFeature. Measured on this tag: DOTNET_SDK_VERSION=10.0.302 - the pinned version
# exactly, so the pin is satisfied without a rollForward hop.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# The repository-root build inputs, first, because they change least often and every project below
# depends on all of them. All five are load-bearing; none is a convenience.
#
#   global.json                 pins the SDK; found by directory walk-up.
#   Directory.Build.props       net10.0, LangVersion latest, nullable, implicit usings, the .NET
#                               analyzers at AnalysisLevel latest, TreatWarningsAsErrors, and
#                               ManagePackageVersionsCentrally. Also the property that makes
#                               `-c Release` propagate into ProjectReference projects the per-service
#                               solution does not list, plus the target that FAILS the build if a
#                               referenced output ever arrives from another configuration. Found by
#                               directory walk-up, which is why the layout below is preserved.
#   Directory.Packages.props    the single source of truth for every package version, including the
#                               two mandatory security pins (Microsoft.OpenApi 2.11.0 and
#                               SQLitePCLRaw.bundle_e_sqlite3 3.0.5). Every PackageReference in this
#                               build graph is versionless, so without this file restore fails
#                               outright rather than quietly choosing a version.
#   .editorconfig               a build input rather than a preference file: it scopes the analyzer
#                               severity overrides for the deliberately preserved SCREAMING_SNAKE
#                               constant identifiers this service carries - EID_ROWFOCUSCHANGE,
#                               CLC_UNKNOWN, FUNC_INVOKE, MID_COLAUTOWIDTH, FILTER_DISP_PY, RS_SINGLE
#                               and the rest. With warnings promoted to errors above, copying it is
#                               what keeps the in-container build diagnostically identical to the
#                               host and CI build instead of failing on a naming analyzer.
#   pfw.i18n.xml                also a build input, and its absence is a hard failure rather than a
#                               missing extra. PowerFramework.Shared.Localization declares a LITERAL,
#                               non-glob Content item for this exact repository-root path with both
#                               CopyToOutputDirectory and CopyToPublishDirectory set; with the file
#                               absent that item fails the build with MSB3030. It is also required at
#                               run time: the ported providers open the table by bare relative
#                               filename beside the built assembly, reproducing the legacy resolution
#                               semantics, and the localization fallback is a SILENT PASSTHROUGH - so
#                               a missing table would degrade quietly rather than announce itself.
#                               The copy must stay byte-identical (6824 bytes, no BOM, LF endings):
#                               a plain COPY preserves every byte and no transform may be added.
#                               This service needs the table because its validation path routes
#                               diagnostics through localization category CAT_DWSVC.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig pfw.i18n.xml ./

# The project manifests ALONE, before any source, so the restore layer is cached against dependency
# changes rather than against every edit to a .cs or .proto file.
#
# These seven are the COMPLETE restore closure of the DataServices application: itself plus the six
# shared projects its .csproj actually declares. Reconciled against that .csproj rather than assumed,
# and the three transitive edges among the six - Diagnostics, Eventful and Localization each
# reference the shared kernel - resolve inside the same set, so nothing further is needed. No sibling
# service manifest appears, and none may.
COPY services/dataservices-service/PowerFramework.DataServices/PowerFramework.DataServices.csproj \
     services/dataservices-service/PowerFramework.DataServices/
COPY shared/PowerFramework.Contracts/PowerFramework.Contracts.csproj \
     shared/PowerFramework.Contracts/
COPY shared/PowerFramework.Shared.Kernel/PowerFramework.Shared.Kernel.csproj \
     shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Containers/PowerFramework.Shared.Containers.csproj \
     shared/PowerFramework.Shared.Containers/
COPY shared/PowerFramework.Shared.Diagnostics/PowerFramework.Shared.Diagnostics.csproj \
     shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Eventful/PowerFramework.Shared.Eventful.csproj \
     shared/PowerFramework.Shared.Eventful/
COPY shared/PowerFramework.Shared.Localization/PowerFramework.Shared.Localization.csproj \
     shared/PowerFramework.Shared.Localization/

# Scoped to the APPLICATION project, deliberately - not to dataservices-service.slnx and not to the
# repository root solution. See "WHAT IS DELIBERATELY ABSENT" above for why each of those is wrong
# here. Restoring the application walks its ProjectReference graph, so all six shared projects are
# restored too; nothing is skipped by narrowing the target.
RUN dotnet restore services/dataservices-service/PowerFramework.DataServices/PowerFramework.DataServices.csproj

# Now the sources. The six shared trees first and the application last, so an edit confined to the
# application does not invalidate the shared layers. Each COPY names one project directory, and the
# .dockerignore keeps bin/, obj/, TestResults/ and coverage output out of the context, so no host
# build output can leak in and defeat the in-container restore.
#
# PowerFramework.Contracts CARRIES ITS Proto/ AND OpenApi/ DIRECTORIES, AND BOTH ARE BUILD INPUTS
# RATHER THAN DOCUMENTATION. That project owns all .proto compilation through a single Protobuf item
# with ProtoRoot="Proto" and GrpcServices="Both", and the generated C# is NOT committed to the
# repository - so the .proto files must be present in this build tree or compilation fails on the
# missing dataservices.v1 server bases this service implements and the persistence.v1 client stubs it
# consumes. The OpenAPI documents are embedded resources in that same assembly, so they must be
# present as well. A directory-level COPY carries both without enumerating either.
COPY shared/PowerFramework.Shared.Kernel/ shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Containers/ shared/PowerFramework.Shared.Containers/
COPY shared/PowerFramework.Shared.Diagnostics/ shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Eventful/ shared/PowerFramework.Shared.Eventful/
COPY shared/PowerFramework.Shared.Localization/ shared/PowerFramework.Shared.Localization/
COPY shared/PowerFramework.Contracts/ shared/PowerFramework.Contracts/
COPY services/dataservices-service/PowerFramework.DataServices/ \
     services/dataservices-service/PowerFramework.DataServices/

# Release configuration, and --no-restore on purpose: it makes an incomplete manifest layer above
# fail loudly here instead of being papered over by an implicit restore that reaches the network
# again. Nothing is trimmed, single-filed or pre-compiled - none of that is required by this
# migration and each would change what ships without a stated reason to (constraint C-B).
RUN dotnet publish services/dataservices-service/PowerFramework.DataServices/PowerFramework.DataServices.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish


# --------------------------------------------------------------------------------------------------
# STAGE 2 - RUNTIME (the one and only final image)
#
# mcr.microsoft.com/dotnet/aspnet:10.0, matched to the Microsoft.AspNetCore.App runtime this refactor
# builds against. Measured on this tag: ASPNET_VERSION=10.0.10 and DOTNET_VERSION=10.0.10.
#
# NOT the sdk image, which carries a compiler, a package cache and a shell toolchain that a running
# service has no use for. NOT the non-ASP.NET `runtime` image either: this is an ASP.NET Core service
# hosting Kestrel, gRPC endpoints, Minimal API endpoints and an OpenAPI document, and it would fail
# to start without the shared framework this image provides.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

# Attribution material, carried inside the image rather than merely referenced by it. The project is
# BSD 2-Clause; its binary-redistribution condition requires the copyright notice, the list of
# conditions and the disclaimer to be reproduced in the materials provided with the distribution, and
# a container image is a binary distribution. NOTICE additionally carries the four-condition
# restatement from the project readme and the eleven upstream attributions. Directory.Build.props
# already advertises both files in the assembly copyright metadata, so an image omitting them would
# point at material it does not carry.
COPY LICENSE NOTICE ./

# The published application. Copied root-owned and world-readable, deliberately: the runtime user
# needs to READ and EXECUTE these files and never to write them, so it is given no ownership of them
# at all. Handing the service write access to its own binaries would be a strictly weaker posture
# than this one.
COPY --from=build /app/publish ./

# THE LISTENING PORT IS 5102, AND IT IS FIXED IN CONFIGURATION RATHER THAN HERE (constraint C-L).
# READ THIS BEFORE ADDING AN ADDRESS VARIABLE BELOW - the obvious "fix" is measurably wrong for this
# service. C-L makes the attached environment's operational shape binding: this service answers on
# 5102, /health is anonymous and /v1/ping requires a bearer token and returns 401 without one. All
# three were verified against the built image, not assumed.
#
# appsettings.json in the published output declares ONE Kestrel endpoint, `https://+:5102` with
# Protocols "Http1AndHttp2", and that single entry is what makes this service's whole contract
# satisfiable on one port: gRPC (contracts C-03 and C-04, C-04 carrying two inverted streams)
# REQUIRES HTTP/2, while the anonymous /health readiness probe and the authenticated /v1/ping are
# HTTP/1.1, and TLS is what lets ALPN negotiate between them per connection. The port map assigns
# this service exactly 5102:
#
#     5101  Persistence     gRPC + REST /health and /v1/ping
#     5102  DataServices    gRPC primary + the thin REST projection for Gateway - THIS IMAGE
#     5103  RESERVED - deliberately unallocated so the obvious Phase 2 slot stays visible rather
#           than being reassigned to one of the four services this phase builds. Nothing is
#           provisioned for it here: no stage, no variable and no build input.
#     5104  Security        REST, and the system's sole token issuer
#     5105  Gateway         REST + OpenAPI, the documented composition-root access URL
#
# SO NO ASPNETCORE_URLS AND NO ASPNETCORE_HTTP_PORTS VALUE IS SET FOR THIS SERVICE. Kestrel binds
# CONFIGURED ENDPOINTS in preference to host addresses whenever PreferHostingUrls is false, which is
# the default, so either variable would be silently ignored - and it would not be silent about it.
# MEASURED on this toolchain against this service's own published output: with a value present
# Kestrel emits, at WARNING level on every single startup,
#
#     Overriding address(es) 'http://*:8080'. Binding to endpoints defined via IConfiguration
#     and/or UseKestrel() instead.
#
# and then binds https://[::]:5102 regardless. Worse than the noise, such a variable would advertise
# a CLEARTEXT scheme this service does not serve, which is exactly the class of defect that leaves a
# caller pointed at a half that cannot answer it.
#
# The instruction below therefore CLEARS the value this base image ships (aspnet:10.0 sets
# ASPNETCORE_HTTP_PORTS=8080) rather than replacing it with 5102. Re-measured after clearing it: the
# override warning disappears, the host binds https://[::]:5102 from configuration, and startup
# reports zero warnings. The documented channel for moving the listener is the configuration key
# itself, Kestrel__Endpoints__Default__Url, because the environment provider is added after
# appsettings.json and wins on an identical key.
#
# THE SERVER CERTIFICATE IS NOT IN THIS IMAGE AND MUST NOT BE (constraint C-F). Kestrel resolves an
# HTTPS endpoint's certificate in a fixed order - an explicit certificate on the endpoint, then
# Kestrel:Certificates:Default, then the local development certificate - and FAILS TO START if it
# finds none. That fail-fast is the intended behaviour: it is a structural fault, never a silent
# downgrade to plaintext. The material is supplied at run time by the orchestration secret layer as
# Kestrel__Certificates__Default__Path and __KeyPath pointing at files mounted read-only into the
# container. No path, store reference or password appears anywhere in this file.
ENV ASPNETCORE_HTTP_PORTS=""

# EXPOSE documents the contract and does not create it; the binding above comes from configuration.
# One port only: the gRPC surface and the thin REST projection share it, so there is nothing to add.
EXPOSE 5102

# Production, stated explicitly rather than relied upon. Unset resolves to Production by framework
# default, so this changes no behaviour on a clean host - it is set because appsettings.Development.
# json IS present in the publish output, and an explicit value means an ambient or inherited
# ASPNETCORE_ENVIRONMENT cannot pull the development overlay into a shipped image by accident. It
# remains overridable: orchestration supplies whichever overlay a local run wants, and that override
# reaches the host normally because a container environment value takes precedence over this default.
ENV ASPNETCORE_ENVIRONMENT=Production

# TWO FRAMEWORK WARNINGS THIS IMAGE EMITS ON EVERY START, AND WHY NOTHING HERE SILENCES THEM.
# Recorded because they are OBSERVED rather than hypothetical, and because a reader who meets them
# without this note will reasonably suspect the container definition is at fault. Measured on a real
# run of this image:
#
#     Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted
#     outside of the container. Protected data will be unavailable when container is destroyed.
#     No XML encryptor configured. Key {...} may be persisted to storage in unencrypted form.
#
# Both come from the ASP.NET Core data-protection key ring, which the framework initialises on its
# own, and the first fires only because the base image sets DOTNET_RUNNING_IN_CONTAINER. NEITHER HAS
# AN OBSERVABLE EFFECT FOR THIS SERVICE, and that is measured too rather than assumed: this service
# registers no data protection consumer at all - zero AddDataProtection, PersistKeysTo or
# IDataProtector uses, and zero cookie authentication, antiforgery or session uses - because its only
# authentication scheme is stateless JWT bearer validation. The ring is created and never used to
# protect a persisted artifact, so losing it when the container is destroyed loses nothing.
#
# The two candidate fixes are both worse than the warnings. Persisting the ring means mounting a
# volume, which is an ORCHESTRATION decision and belongs in the compose manifest, not baked into an
# image that must be identical in every deployment. Configuring an XML encryptor means introducing
# key material or a key-vault reference here, which is precisely the secret surface constraint C-F
# forbids this file to have. So this is a documented deliberate non-action, not an oversight, and it
# is the last place either should be "fixed".

# NON-ROOT, SWITCHED BEFORE THE ENTRY POINT.
# APP_UID is defined by the base image as 1654 and corresponds to the unprivileged `app` user and
# group that image already provisions, with a home directory the user can write. Using the variable
# rather than a hardcoded number is the mechanism the base image publishes for exactly this purpose,
# and it cannot drift from the identity that image actually creates. Everything after this line - the
# health probe and the application itself - runs unprivileged. Non-root execution is part of the
# constraint C-G security posture, not a nicety.
#
# The suppression below is scoped to this one rule on this one line, and it is stated rather than
# left as a standing finding. DL3066 warns that a non-numeric user id may be unresolvable by the
# host; a linter cannot see the base image's environment, so it reads the variable as non-numeric.
# On the built image the resolved value is numeric AND backed by a real account: the image config
# records user 1654, `id -u` returns 1654, and `getent passwd 1654` resolves to app. The rule's
# concern does not apply here.
# hadolint ignore=DL3066
USER $APP_UID

# READINESS PROBE - THE ANONYMOUS GET /health OVER TLS, AND NOTHING AUTHENTICATED
# /health is anonymous on all four services; /v1/ping requires a bearer token and answers 401 without
# one, so a probe must never point at it. This service's /health reports on ITSELF and does not
# aggregate upstreams - verified by running this image with neither Persistence nor Security present
# and receiving 200 - which is precisely what lets it serve as a satisfied upstream in the compose
# `depends_on: condition: service_healthy` chain that gates Gateway behind it. A compose manifest
# should INHERIT this healthcheck rather than declare its own.
#
# WHY THIS PROBE IS NOT THE SAME AS THE GATEWAY IMAGE'S, AND MUST NOT BE COPIED FROM IT: Gateway
# listens in cleartext, so a bash /dev/tcp socket write is enough there. THIS listener is
# TLS-terminated, and /dev/tcp cannot perform a TLS handshake - a probe built that way would fail
# permanently and leave the readiness chain unsatisfiable. Measured in this base image: curl and wget
# are both ABSENT, and installing one to obtain a probe binary is not acceptable, but openssl 3.0.13
# IS present, so `openssl s_client` performs the handshake and the request with nothing installed.
#
# Verification is intentionally left at its default, NOT hardened: the certificate is a private
# loopback server certificate supplied per deployment and is commonly self-signed, so `s_client`
# reports the verification result on stderr and proceeds, which is the correct behaviour for a probe
# testing whether the service answers rather than whether a public chain validates. -verify_return_
# error is deliberately not passed. -servername keeps SNI populated as a real client would.
#
# The status line is matched on ' 200 ' rather than a bare 200 so a 200 appearing elsewhere in a
# reason phrase cannot pass. A non-200 status, a refused connection, a failed handshake and a silent
# socket all fail. The inner timeout is shorter than --timeout so the probe is bounded twice. The
# port is written literally because this service's address lives in configuration rather than in an
# environment variable there would be to read back; if a deployment moves the listener with
# Kestrel__Endpoints__Default__Url it must override this healthcheck too, and pinning the probe to
# the port the contract fixes is preferable to letting it silently follow an unknown address.
#
# `-quiet` implies -ign_eof, and that matters: passing -no_ign_eof alongside it makes s_client tear
# the connection down the instant stdin reaches EOF, which is BEFORE the response arrives, and the
# probe then reads nothing and fails against a perfectly healthy service. Measured, not reasoned
# about. The request carries `Connection: close`, so the server ends the session and the pipeline
# terminates on its own.
HEALTHCHECK --interval=15s --timeout=10s --start-period=20s --retries=5 \
  CMD ["/bin/bash", "-c", "line=$(printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' | timeout 5 openssl s_client -quiet -verify_quiet -servername localhost -connect 127.0.0.1:5102 2>/dev/null | head -n 1); case $line in *' 200 '*) exit 0 ;; *) exit 1 ;; esac"]

# The framework-dependent application assembly, invoked by name. PowerFramework.DataServices.dll is
# fixed by the AssemblyName in PowerFramework.DataServices.csproj; renaming either without the other
# breaks this entry point. Exec form, so the runtime is PID 1 and receives SIGTERM directly - which
# is what lets the host run its shutdown path, and with it the ordered release of the validation
# sessions and then the expression sessions that this service holds on behalf of its callers.
ENTRYPOINT ["dotnet", "PowerFramework.DataServices.dll"]
