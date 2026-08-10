# ==================================================================================================
#  services/gateway-service/Dockerfile - THE CONTAINER IMAGE FOR THE GATEWAY SERVICE
#  ------------------------------------------------------------------------------------------------
#  ONE IMAGE, FOR ONE SERVICE. Gateway is the system's sole ingress and composition root, and this
#  file produces its image and nothing else: two stages, exactly one final image, no variant tag and
#  no second final stage. Each of the three sibling services carries its own Dockerfile; none of
#  them is copied, restored or built here, and Gateway reaches them over the network through the
#  published contracts rather than through anything baked into this image.
#
#  LEGACY REFERENCE - READ FOR BUILD INTENT ONLY, NEVER TRANSLATED
#  The two PowerBuilder project objects both declare `EXE:pfw.exe`: ONE fused executable linking 28
#  or 29 compiled libraries into a single process. That is the artifact this decomposition replaces,
#  and it is the whole reason there is one image per service rather than one image for the estate.
#  Neither object is translated and neither could be: they disagree on library count (29 entries
#  including pfw.utility.sqlite.pbl, versus 28 omitting it) and on vendor (`COM:Appeon` versus
#  `COM:Sybase, Inc.`), one embeds developer-workstation absolute paths (`OBJ:F:\pfw\...`) together
#  with the retired pfwUtils.pbl, and BOTH name pfw.utility.imgcodec.pbl, which exists nowhere in
#  this repository - so neither would build as written. No library list, version or vendor string
#  below derives from either of them.
#
#  Nothing in the read-only legacy tree enters this image. No COPY or ADD instruction names
#  ws_objects/, a *.pbl, *.pbt, *.pbw, *.pbr, *.pbd or *.pbg file, or any of the seven root native
#  binaries (pfw.dll, pfwx.dll, blink.dll, blinkfast.dll, sciter.dll, sqlite3.dll,
#  sqlite3.cipher.dll). The repository-root .dockerignore excludes all of them from the build
#  context; every COPY below names an allowed path explicitly rather than relying on a blanket
#  `COPY . .`, so the contents of this image are legible and auditable from this file alone.
#
#  THE BUILD CONTEXT IS THE REPOSITORY ROOT, NOT THIS DIRECTORY
#  Build it as:  docker build -f services/gateway-service/Dockerfile .   (from the repository root)
#  Compose declares it as:  context: ..   dockerfile: services/gateway-service/Dockerfile
#
#  This is structural, not stylistic. PowerFramework.Gateway.csproj carries ProjectReference entries
#  that rise three levels into shared/ - PowerFramework.Contracts, PowerFramework.Shared.Kernel,
#  PowerFramework.Shared.Diagnostics and PowerFramework.Shared.Localization - and MSBuild resolves
#  Directory.Build.props, Directory.Packages.props and global.json by walking UP from each project
#  to the repository root. A context scoped to services/gateway-service/ can see none of that and
#  restore fails. Narrowing the context looks tidier and does not work. The .dockerignore says the
#  same thing from the other side: it exists because each service's Dockerfile lives at
#  services/<name>/Dockerfile yet must restore the shared/ projects.
#
#  Every source path in every COPY below is therefore REPOSITORY-ROOT-RELATIVE.
#
#  WHAT IS DELIBERATELY ABSENT FROM THIS FILE
#    * No test project. PowerFramework.Gateway.Tests is the producer of Gateway's
#      coverage.cobertura.xml and it runs in CI, not in the shipped image. gateway-service.slnx
#      enumerates it alongside the application, which is exactly why the build below targets the
#      APPLICATION .csproj directly rather than the solution: publishing the solution would drag the
#      test project, xunit.v3, the test SDK and the coverage collector into the build for nothing.
#    * No .slnf solution filter, anywhere, ever. Legacy solution filters are incompatible with the
#      .slnx XML solution format and fail with MSB4014 during restore, build and test.
#    * No NuGet.config. The repository has none; naming a path that does not exist fails the COPY.
#    * No docs/, .github/, orchestration/, tests/e2e/ or characterization/ path. All are excluded
#      from the build context, so referencing any of them would break the build.
#    * No secret, in any form. No ENV and no ARG below carries a key, token, password, certificate,
#      passphrase or connection credential. SECURITY_JWT_SIGNING_KEY is the system's single signing
#      secret and belongs to the Security service alone; Gateway holds verification material only,
#      so no signing key of any spelling appears here, and no mutual-TLS path is baked in either -
#      that material is MOUNTED from the orchestration secret layer at run time and reaches the
#      application through the options pattern.
#    * Nothing for a deferred capability. DesignSystem, Documents, Integration and ScriptBridge have
#      no stage, layer, label, environment variable or build input here. Their four reserved routes
#      are routing metadata inside the application and nothing compiles or ships behind them.
#    * No extra tooling. No shell utility, package, debugging agent or telemetry sidecar is installed
#      into either stage; see the HEALTHCHECK note for how the readiness probe manages without one.
#    * No performance, latency, throughput or availability claim. The repository publishes no such
#      budget, so none is asserted here or used to justify a choice below.
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
# depends on all of them.
#
#   global.json                 pins the SDK; found by directory walk-up.
#   Directory.Build.props       net10.0, nullable, implicit usings, warnings-as-errors, analyzers,
#                               and ManagePackageVersionsCentrally; found by directory walk-up.
#   Directory.Packages.props    the single source of truth for every package version, including the
#                               two mandatory security pins. Every PackageReference in this build
#                               graph is versionless, so without this file restore fails outright.
#   .editorconfig               a build input rather than a preference file: it scopes the analyzer
#                               severity overrides for the deliberately preserved SCREAMING_SNAKE
#                               constant identifiers. Copying it is what makes the in-container build
#                               diagnostically identical to the host and CI build.
#   pfw.i18n.xml                also a build input. PowerFramework.Shared.Localization declares a
#                               literal, non-glob Content item for this exact path; with the file
#                               absent that item fails the build with MSB3030. It is read-only legacy
#                               DATA that the .dockerignore keeps in the context for precisely this
#                               reason, and the localization providers open it by bare relative
#                               filename beside the built assembly at run time.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig pfw.i18n.xml ./

# The project manifests ALONE, before any source, so the restore layer is cached against dependency
# changes rather than against every edit to a .cs file. These five are the complete restore closure
# of the Gateway application: itself plus the four shared projects it references. The two transitive
# edges among those four - Diagnostics and Localization each reference the shared kernel - resolve
# inside the same set, so nothing further is needed. No sibling service manifest appears, and none
# may: Gateway must restore, build and publish with no other service present.
COPY services/gateway-service/PowerFramework.Gateway/PowerFramework.Gateway.csproj \
     services/gateway-service/PowerFramework.Gateway/
COPY shared/PowerFramework.Contracts/PowerFramework.Contracts.csproj \
     shared/PowerFramework.Contracts/
COPY shared/PowerFramework.Shared.Kernel/PowerFramework.Shared.Kernel.csproj \
     shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Diagnostics/PowerFramework.Shared.Diagnostics.csproj \
     shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Localization/PowerFramework.Shared.Localization.csproj \
     shared/PowerFramework.Shared.Localization/

RUN dotnet restore services/gateway-service/PowerFramework.Gateway/PowerFramework.Gateway.csproj

# Now the sources. The four shared trees first and the application last, so an edit confined to the
# application does not invalidate the shared layers. Each COPY names one project directory: the
# .dockerignore keeps bin/, obj/ and TestResults/ out, so no host build output can leak in.
COPY shared/PowerFramework.Shared.Kernel/ shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Diagnostics/ shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Localization/ shared/PowerFramework.Shared.Localization/
COPY shared/PowerFramework.Contracts/ shared/PowerFramework.Contracts/
COPY services/gateway-service/PowerFramework.Gateway/ services/gateway-service/PowerFramework.Gateway/

# Release configuration, and --no-restore on purpose: it makes an incomplete manifest layer above
# fail loudly here instead of being papered over by an implicit restore that reaches the network
# again. Nothing is trimmed, single-filed or pre-compiled - none of that is required by this
# migration and each would change what ships without a stated reason to.
RUN dotnet publish services/gateway-service/PowerFramework.Gateway/PowerFramework.Gateway.csproj \
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
# service has no use for. NOT the non-ASP.NET `runtime` image either: Gateway is an ASP.NET Core
# service and would fail to start without the shared framework this image provides.
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

# THE LISTENING PORT - 5105, AND WHY IT IS SET HERE RATHER THAN IN THE APPLICATION
# 5105 is the documented composition-root access URL for the whole system (http://localhost:5105),
# and the 5101-5105 band is retained with Gateway keeping its documented port:
#
#     5101  Persistence     gRPC + REST /health and /v1/ping
#     5102  DataServices    gRPC primary + a thin REST projection for Gateway
#     5103  RESERVED - deliberately unallocated so the obvious Phase 2 slot stays visible, rather
#           than reassigned to one of the four services this phase builds. Nothing is provisioned
#           for it here: no stage, no variable and no build input.
#     5104  Security        REST, and the system's sole token issuer
#     5105  Gateway         REST + OpenAPI - THIS IMAGE
#
# Gateway declares no listening port in code and carries no Kestrel section in appsettings.json, so
# the container supplies the address; this ENV is that supply, and it also overrides the base image's
# own ASPNETCORE_HTTP_PORTS=8080. The published ingress is plaintext HTTP, which is why no server
# certificate variable appears here. The three internal listeners terminate TLS instead and fix their
# own address in configuration, so this instruction is correct for Gateway specifically and must not
# be copied into a sibling service's Dockerfile.
#
# ASPNETCORE_ENVIRONMENT is deliberately NOT set. Leaving it unset selects the framework default,
# Production; which overlay a deployment wants is a deployment decision that orchestration supplies.
ENV ASPNETCORE_HTTP_PORTS=5105
EXPOSE 5105

# NON-ROOT, SWITCHED BEFORE THE ENTRY POINT.
# APP_UID is defined by the base image as 1654 and corresponds to the unprivileged `app` user and
# group that image already provisions, with a home directory the user can write. Using the variable
# rather than a hardcoded number is the mechanism the base image publishes for exactly this purpose,
# and it cannot drift from the identity that image actually creates.
# Everything after this line - the health probe and the application itself - runs unprivileged.
#
# The suppression below is scoped to this one rule on this one line, and it is stated rather than
# left as a standing finding. DL3066 warns that a non-numeric user id may be unresolvable by the
# host; a linter cannot see the base image's environment, so it reads the variable as non-numeric.
# On the built image the resolved value is numeric AND backed by a real account: the image config
# records user 1654, `id -u` returns 1654, and `getent passwd 1654` resolves to app. The rule's
# concern does not apply here.
# hadolint ignore=DL3066
USER $APP_UID

# READINESS PROBE - THE ANONYMOUS GET /health, AND NOTHING AUTHENTICATED
# /health is anonymous on all four services; /v1/ping requires a bearer token and answers 401
# without one, so a probe must never point at it. Gateway's /health AGGREGATES its three upstreams
# and answers 200 only once Persistence, DataServices and Security are all healthy, answering 503
# until then - which is the readiness gate a compose `depends_on: condition: service_healthy` chain
# is built on. Requiring 200 here is therefore what makes that gate mean something.
#
# WHY NO curl AND NO wget: this base image ships neither, and installing one would be adding tooling
# to a runtime image for a probe's sake. The probe below uses only what the image already has - bash,
# its /dev/tcp socket redirection, and printf - so nothing is installed and nothing is skipped. A
# compose manifest should INHERIT this healthcheck rather than declare a curl-based one, which in
# this image could never succeed and would leave the readiness chain permanently unsatisfied.
#
# The port is read back from ASPNETCORE_HTTP_PORTS so that overriding the listener moves the probe
# with it; 5105 is the fallback and matches the ENV above. A non-200 status line, a refused
# connection or a silent socket all fail, and the status line is matched on " 200 " rather than a
# bare 200 so a 200 appearing elsewhere in the reason phrase cannot pass.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
  CMD ["/bin/bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/${ASPNETCORE_HTTP_PORTS:-5105} && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && read -r -t 4 line <&3 && case $line in *' 200 '*) exit 0 ;; *) exit 1 ;; esac"]

# The framework-dependent application assembly, invoked by name. PowerFramework.Gateway.dll is fixed
# by the AssemblyName in PowerFramework.Gateway.csproj; renaming either without the other breaks this
# entry point. Exec form, so the runtime is PID 1 and receives SIGTERM directly - which is what lets
# the host run its shutdown path, and with it the framework finalize call that the legacy
# documentation requires to be paired with initialize.
ENTRYPOINT ["dotnet", "PowerFramework.Gateway.dll"]
