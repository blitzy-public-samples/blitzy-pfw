# ==================================================================================================
#  services/persistence-service/Dockerfile - THE CONTAINER IMAGE FOR THE PERSISTENCE SERVICE
#  ------------------------------------------------------------------------------------------------
#  ONE IMAGE, FOR ONE SERVICE. Persistence is the only service that generates or executes SQL and the
#  only one holding a storage provider, and this file produces its image and nothing else: two
#  stages, exactly one final image, no variant tag and no second final stage. Each of the three
#  sibling services carries its own Dockerfile; none of them is copied, restored or built here, and
#  nothing reaches Persistence except DataServices, over gRPC, through the published contracts.
#
#  WHY THIS FILE IS THE LAST OF THE FOUR TO EXIST. It is the container-image half of the per-service
#  CI matrix: .github/workflows/ci.yml builds one image per in-scope service per build, which it
#  cannot do uniformly while one of the four has no Dockerfile. The AAP names
#  services/persistence-service/Dockerfile as a create target, so this is that target rather than a
#  new one, and its shape is taken from the Security sibling element for element because both
#  services terminate TLS on their own listener.
#
#  LEGACY REFERENCE - READ FOR BUILD INTENT ONLY, NEVER TRANSLATED
#  The two PowerBuilder project objects both declare `EXE:pfw.exe`: ONE fused executable linking 28
#  or 29 compiled libraries into a single process, with the SQL and threading libraries among them.
#  That is the artifact this decomposition replaces, and it is the whole reason there is one image
#  per service. Neither object is translated and neither could be: they disagree on library count
#  (29 entries INCLUDING pfw.utility.sqlite.pbl versus 28 omitting it - a disagreement about
#  precisely this service's storage binding), they disagree on vendor, and both name
#  pfw.utility.imgcodec.pbl, which exists nowhere in this repository, so neither would build as
#  written. No library list, version or vendor string below derives from either of them.
#
#  Nothing in the read-only legacy tree enters this image. No COPY or ADD instruction names
#  ws_objects/, a *.pbl, *.pbt, *.pbw, *.pbr, *.pbd or *.pbg file, or any of the seven root native
#  binaries - and that last exclusion matters more here than anywhere else, because two of them are
#  sqlite3.dll and sqlite3.cipher.dll. This service does NOT ship or load either: its SQLite comes
#  from the SQLitePCLRaw.bundle_e_sqlite3 package, whose native asset the publish step below places
#  beside the managed assemblies. The legacy pair are Windows binaries of materially different
#  vintages and the cipher-enabled one is a 2019 build whose key derivation and per-page integrity
#  options no framework API exposes; encrypted-database parity is out of Phase 1 scope for that
#  reason, so no cipher library, key or passphrase appears in this file.
#
#  THE BUILD CONTEXT IS THE REPOSITORY ROOT, NOT THIS DIRECTORY
#  Build it as:  docker build -f services/persistence-service/Dockerfile .   (from the repository root)
#  Compose declares it as:  context: ..   dockerfile: services/persistence-service/Dockerfile
#
#  This is structural, not stylistic. PowerFramework.Persistence.csproj carries ProjectReference
#  entries that rise three levels into shared/ - PowerFramework.Contracts,
#  PowerFramework.Shared.Kernel, PowerFramework.Shared.Diagnostics and
#  PowerFramework.Shared.Containers - and MSBuild resolves Directory.Build.props,
#  Directory.Packages.props and global.json by walking UP from each project to the repository root. A
#  context scoped to services/persistence-service/ can see none of that and restore fails. Narrowing
#  the context looks tidier and does not work.
#
#  Every source path in every COPY below is therefore REPOSITORY-ROOT-RELATIVE.
#
#  WHAT IS DELIBERATELY ABSENT FROM THIS FILE
#    * No test project. PowerFramework.Persistence.Tests is the producer of this service's
#      coverage.cobertura.xml and it runs in CI, not in the shipped image. persistence-service.slnx
#      enumerates it alongside the application, which is exactly why the build below targets the
#      APPLICATION .csproj directly rather than the solution: publishing the solution would drag the
#      test project, xunit.v3, the test SDK and the coverage collector into the build for nothing.
#    * No pfw.i18n.xml. Unlike Gateway and DataServices, this service references no localization
#      project, so the literal Content item that makes that file a build input is not in this restore
#      closure. Copying it would put read-only legacy data into an image with no consumer for it.
#    * No .slnf solution filter, anywhere, ever. Legacy solution filters are incompatible with the
#      .slnx XML solution format and fail with MSB4014 during restore, build and test.
#    * No NuGet.config. The repository has none; naming a path that does not exist fails the COPY.
#    * No docs/, .github/, orchestration/, tests/e2e/ or characterization/ path. All are excluded
#      from the build context, so referencing any of them would break the build.
#    * No secret, in any form. No ENV and no ARG below carries a key, token, password, certificate,
#      passphrase or connection credential. SECURITY_JWT_SIGNING_KEY belongs to the Security service
#      alone and Persistence holds verification material only - it fetches the published JWKS over
#      the network. The SQLite URI grammar this service reproduces admits an optional password
#      parameter; none is baked here, and the transaction descriptor's log password is write-only by
#      construction. Any such material is MOUNTED from the orchestration secret layer at run time and
#      reaches the application through the options pattern.
#    * No database FILE, and no seed data. The image carries the schema as EF Core migrations inside
#      the published assembly and nothing else; the database itself lives on the mounted volume
#      described below, which is what makes a paired characterization capture against one unrecreated
#      volume possible at all.
#    * Nothing for a deferred capability. DesignSystem, Documents, Integration and ScriptBridge have
#      no stage, layer, label, environment variable or build input here.
#    * No SQL Server or Oracle client, driver or connection string. Both DBMS behaviours this service
#      preserves are PURE STRING TRANSFORMS in the paging rewriters, unit-tested with no instance of
#      either kind, and no schema, connection string or DDL for either exists anywhere in the
#      repository. Installing a client for one would fabricate a database this refactor forbids.
#    * No performance, latency, throughput or availability claim. The repository publishes no such
#      budget, so none is asserted here or used to justify a choice below.
# ==================================================================================================


# --------------------------------------------------------------------------------------------------
# STAGE 1 - BUILD
#
# mcr.microsoft.com/dotnet/sdk:10.0, matched to the SDK global.json pins (10.0.302, rollForward
# latestFeature). The family tag is used deliberately so that a security-patched rebuild picks the
# fix up; the reproducibility that matters is pinned where it can be, in global.json and in
# Directory.Packages.props.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# The repository-root build inputs, first, because they change least often and every project below
# depends on all of them.
#
#   global.json                 pins the SDK; found by directory walk-up.
#   Directory.Build.props       net10.0, nullable, implicit usings, warnings-as-errors, analyzers,
#                               and ManagePackageVersionsCentrally; found by directory walk-up. It
#                               also carries the configuration-propagation property without which a
#                               Release build of this project would consume Debug shared assemblies.
#   Directory.Packages.props    the single source of truth for every package version, including the
#                               two mandatory security pins - one of which,
#                               SQLitePCLRaw.bundle_e_sqlite3 3.0.5, exists precisely to override the
#                               vulnerable native bundle the EF Core SQLite provider would otherwise
#                               pull into THIS image. Every PackageReference in this build graph is
#                               versionless, so without this file restore fails outright.
#   .editorconfig               a build input rather than a preference file: it scopes the analyzer
#                               severity overrides for the deliberately preserved SCREAMING_SNAKE
#                               constant identifiers, of which this service carries the SQL_MS_* and
#                               DBT_* sets. Copying it is what makes the in-container build
#                               diagnostically identical to the host and CI build.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./

# The project manifests ALONE, before any source, so the restore layer is cached against dependency
# changes rather than against every edit to a .cs file. These five are the complete restore closure
# of the Persistence application: itself plus the four shared projects it references. The one
# transitive edge among those four - Diagnostics references the shared kernel - resolves inside the
# same set, so nothing further is needed. No sibling service manifest appears, and none may:
# Persistence must restore, build and publish with no other service present.
COPY services/persistence-service/PowerFramework.Persistence/PowerFramework.Persistence.csproj \
     services/persistence-service/PowerFramework.Persistence/
COPY shared/PowerFramework.Contracts/PowerFramework.Contracts.csproj \
     shared/PowerFramework.Contracts/
COPY shared/PowerFramework.Shared.Kernel/PowerFramework.Shared.Kernel.csproj \
     shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Diagnostics/PowerFramework.Shared.Diagnostics.csproj \
     shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Containers/PowerFramework.Shared.Containers.csproj \
     shared/PowerFramework.Shared.Containers/

RUN dotnet restore services/persistence-service/PowerFramework.Persistence/PowerFramework.Persistence.csproj

# Now the sources. The four shared trees first and the application last, so an edit confined to the
# application does not invalidate the shared layers. Each COPY names one project directory: the
# .dockerignore keeps bin/, obj/ and TestResults/ out, so no host build output can leak in.
COPY shared/PowerFramework.Shared.Kernel/ shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Diagnostics/ shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Containers/ shared/PowerFramework.Shared.Containers/
COPY shared/PowerFramework.Contracts/ shared/PowerFramework.Contracts/
COPY services/persistence-service/PowerFramework.Persistence/ services/persistence-service/PowerFramework.Persistence/

# Release configuration, and no-restore on purpose: it makes an incomplete manifest layer above fail
# loudly here instead of being papered over by an implicit restore that reaches the network again.
# Nothing is trimmed, single-filed or pre-compiled - none of that is required by this migration and
# each would change what ships without a stated reason to. Trimming in particular would be actively
# unsafe here, because the EF Core provider and the SQLitePCLRaw bundle resolve types by reflection.
RUN dotnet publish services/persistence-service/PowerFramework.Persistence/PowerFramework.Persistence.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish


# --------------------------------------------------------------------------------------------------
# STAGE 2 - RUNTIME (the one and only final image)
#
# mcr.microsoft.com/dotnet/aspnet:10.0, matched to the Microsoft.AspNetCore.App runtime this refactor
# builds against.
#
# NOT the sdk image, which carries a compiler, a package cache and a shell toolchain that a running
# service has no use for. NOT the non-ASP.NET `runtime` image either: this service hosts gRPC over
# Kestrel and would fail to start without the shared framework this image provides.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# curl, for the readiness probe below and for nothing else.
#
# This base image ships neither curl nor wget. Every listener in this estate terminates TLS, so a raw
# socket cannot complete a handshake and bash /dev/tcp cannot serve as a probe here. The Gateway,
# DataServices and Security siblings answer that with the openssl s_client pipeline the base image does
# ship; this definition installs one package instead and probes with curl, which is the one place in the
# four that accepts an install rather than working around it - the chain is verified either way, and the
# curl form is the readable one. Without a usable probe a compose
# `depends_on: condition: service_healthy` gate on this service could never be satisfied.
#
# Kept as small as the requirement allows: one package, no recommended extras, and the package lists
# removed in the SAME layer so nothing is cached into the image. Installed BEFORE the USER switch
# below because apt-get needs root; everything after that line runs unprivileged.
#
# The apt version is deliberately not pinned, and that is a considered decision rather than an
# oversight. The base image is referenced by FAMILY tag so its package set advances with every
# security-patched rebuild; pinning one apt version against a moving base fails the build the moment
# the archive supersedes that version. The reproducibility that matters is pinned where it can be:
# the SDK in global.json and every package version in Directory.Packages.props.
# hadolint ignore=DL3008
RUN apt-get update \
  && apt-get install --yes --no-install-recommends curl \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Attribution material, carried INSIDE the image rather than merely referenced by it. The project is
# BSD 2-Clause; its binary-redistribution condition requires the copyright notice, the list of
# conditions and the disclaimer to be reproduced in the materials provided with the distribution, and
# a container image is a binary distribution. NOTICE additionally carries the four-condition
# restatement from the project readme and the eleven upstream attributions - one of which is SQLite
# itself, which makes carrying it here more than a formality. Directory.Build.props already
# advertises both files in the assembly copyright metadata, so an image omitting them would point at
# material it does not carry.
COPY LICENSE NOTICE ./

# The published application. Copied root-owned and world-readable, deliberately: the runtime user
# needs to READ and EXECUTE these files and never to write them, so it is given no ownership of them
# at all. Handing the service write access to its own binaries would be a strictly weaker posture.
COPY --from=build /app/publish ./

# THE DATABASE DIRECTORY - CREATED HERE, OWNED BY THE RUNTIME USER, MOUNTED AT RUN TIME
#
# /var/lib/powerframework is the DataDirectory default in appsettings.json, and the database file
# beneath it is test.db - the legacy name, retained deliberately because it is the name the
# behavioural oracle opens [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456].
#
# The directory is created and chowned to the unprivileged account BEFORE the USER switch, because
# this is the one path the service must be able to WRITE: SQLite creates the database file on first
# open under mode=rwc, and it also creates and removes a rollback journal beside it, so write
# permission on the DIRECTORY rather than merely on the file is required. A container that could not
# write here would fail its first open rather than degrade, which is the correct behaviour but a
# needlessly obscure way to discover a permissions mistake.
#
# It is declared a VOLUME so that the database survives the container, which is not a convenience:
# the paired characterization capture rule requires the legacy-side and target-side recordings for
# one workflow identifier to be taken against the SAME volume state, with the volume neither
# recreated nor reseeded between them. An image that kept the database in its writable layer would
# make that rule impossible to honour. Compose supplies the named `persistence-db` volume here.
RUN mkdir -p /var/lib/powerframework \
  && chown -R $APP_UID:$APP_UID /var/lib/powerframework
VOLUME ["/var/lib/powerframework"]

# THE LISTENING PORTS - TWO OF THEM, DOCUMENTED HERE AND ESTABLISHED IN CONFIGURATION
#
# ASPNETCORE_HTTP_PORTS is cleared rather than set. appsettings.json declares TWO named Kestrel
# endpoints, and each carries a protocol set a bare port number cannot express:
#
#     Rest  https://+:5101  Http1   - /health and /v1/ping
#     Grpc  https://+:5111  Http2   - the four published gRPC contracts, C-05 through C-08
#
# ONE PROTOCOL VERSION EACH, RATHER THAN Http1AndHttp2 ON ONE PORT, and that is a measured decision
# rather than a preference: on a TLS endpoint ALPN negotiates between the two versions, but the
# distinct endpoints keep the readiness probe reachable over HTTP/1.1 with no dependence on ALPN
# behaviour in whatever proxy or client sits in front of it. A deployment that prefers one address
# collapses them by pointing Kestrel__Endpoints__Rest__Url at https://+:5101 with Protocols
# Http1AndHttp2 and clearing the Grpc endpoint - the environment provider is added after
# appsettings.json and wins on an identical key, so that needs no change to this file.
#
# Leaving ASPNETCORE_HTTP_PORTS populated, including with the base image's own 8080, would add a
# SECOND, PLAINTEXT listener beside the configured pair, so it is emptied. EXPOSE therefore DOCUMENTS
# the contract rather than establishing it. 5101 is this service's slot in the retained 5101-5105
# band; 5111 is internal to the orchestration network and is not named by the attached environment:
#
#     5101  Persistence     REST /health and /v1/ping over TLS - THIS IMAGE (gRPC on 5111)
#     5102  DataServices    REST projection for Gateway over TLS (gRPC on 5112)
#     5103  RESERVED - deliberately unallocated so the obvious Phase 2 slot stays visible rather than
#           being reassigned to one of the four services this phase builds. Nothing is provisioned
#           for it here: no stage, no variable, no build input.
#     5104  Security        REST + OpenAPI over TLS, and the system's SOLE token issuer
#     5105  Gateway         REST + OpenAPI over TLS, the documented composition-root access URL
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 5101 5111

# NON-ROOT, SWITCHED BEFORE THE PROBE AND BEFORE THE ENTRY POINT.
# APP_UID is published by the base image as 1654 and corresponds to the unprivileged `app` user and
# group that image already provisions. Using the variable the base image publishes rather than a
# hardcoded number is the mechanism provided for exactly this purpose and it cannot drift from the
# identity the image actually creates. Everything after this line, the health probe and the
# application alike, runs unprivileged - and in the image holding the only storage provider in the
# system that is the difference between a contained fault and a root-owned one.
#
# The suppression below is scoped to this one rule on this one line and is stated rather than left as
# a standing finding. DL3066 warns that a non-numeric user id may be unresolvable on the host; a
# linter cannot see the base image's environment, so it reads the variable as non-numeric. On the
# built image the resolved value is numeric AND backed by a real account, so the concern does not
# apply here.
# hadolint ignore=DL3066
USER $APP_UID

# READINESS PROBE - THE ANONYMOUS GET /health OVER TLS, AND NOTHING AUTHENTICATED
#
# /health is anonymous on all four services; /v1/ping requires a bearer token and answers 401
# without one, so a probe must never point at it. Gateway reports healthy only once this service,
# DataServices and Security all do, so this probe is one of the three gates that readiness chain
# rests on.
#
# --cacert AND NOT --insecure, and the reasoning that once favoured --insecure is what settles it. A
# self-check inside the container has no party to authenticate, true - but the thing this probe gates is
# not "did a socket answer", it is the Compose `depends_on: condition: service_healthy` edge that lets
# DataServices start calling this service. A listener presenting a certificate the REST OF THE STACK
# rejects is not ready by that definition, and --insecure is precisely the flag that hides it: the probe
# would go green while every real caller failed name or chain validation. Verifying against the same
# mounted anchor every consumer uses is what makes a green probe mean what the gate needs it to mean, and
# it bakes nothing into the image - the anchor is a read-only mount named by one variable, exactly as the
# three sibling images name it. docs/ARCHITECTURE.md 4.2 states the rule this now follows: -k must not
# appear in a readiness gate.
#
# --fail turns anything that is not a success status into a failed probe, which matters because
# /health answers 503 for Degraded and Unhealthy alike rather than refusing the connection.
#
# 5101 is written literally rather than read from a variable, because the port is fixed by the
# published contract - appsettings.json, docs/ARCHITECTURE.md and orchestration/.env.example all
# state it - and introducing a variable only this probe would read means adding configuration
# surface nothing else consumes. A deployment that moves the listener through Kestrel's own
# configuration has to move this probe with it.
#
# The start period is unhurried: this host validates its configuration, opens its database and
# applies pending migrations before it answers anything, and failures inside that window do not
# count against the retry budget.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["/bin/bash", "-c", "exec curl --fail --silent --show-error --max-time 4 --output /dev/null \
    --cacert \"${INTERNAL_TLS_TRUSTED_CA_PATH:-/run/secrets/internal-tls/ca.crt}\" \
    --resolve localhost:5101:127.0.0.1 https://localhost:5101/health"]

# The framework-dependent application assembly, invoked by name. PowerFramework.Persistence.dll is
# fixed by the project file name, because PowerFramework.Persistence.csproj declares no AssemblyName
# override, so renaming the project without updating this line breaks the entry point.
#
# Exec form, so the runtime is PID 1 and receives SIGTERM directly instead of through a shell that
# would swallow it. That is what lets the host run its shutdown path, which disposes the transaction
# pool and the connection factory - this service's structural equivalent of the finalize the legacy
# framework requires to be paired with its initialize [ws_objects/pfw.pbl.src/pfw.sra:L91 against
# :L108]. A pool torn down by SIGKILL instead would leave a rollback journal beside the database.
#
# No argument is passed and none is needed. Configuration reaches the host from appsettings.json, the
# environment-selected overlay and the environment itself, in that order, and a command-line value
# would be a fourth ingress for settings that already have one.
ENTRYPOINT ["dotnet", "PowerFramework.Persistence.dll"]
