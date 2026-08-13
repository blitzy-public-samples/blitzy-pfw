# ==================================================================================================
#  services/persistence-service/Dockerfile - THE CONTAINER IMAGE FOR THE PERSISTENCE SERVICE
#  ------------------------------------------------------------------------------------------------
#  ONE IMAGE, FOR ONE SERVICE. Persistence is the only service that generates or executes SQL and the
#  only one holding a storage provider, and this file produces its image and nothing else: two
#  stages, exactly one final image, no variant tag and no second final stage. Each of the three
#  sibling services carries its own Dockerfile; none of them is copied, restored or built here, and
#  nothing reaches Persistence except DataServices, over gRPC, through the published contracts.
#
#  WHY THIS FILE EXISTS. It is the container-image half of the per-service CI matrix:
#  .github/workflows/ci.yml builds one image per in-scope service per build, which it can only do
#  uniformly while every one of the four services carries a definition of its own. The AAP names
#  services/persistence-service/Dockerfile as a create target, so this is that target rather than a new
#  one, and its shape follows the Security sibling element for element: both services terminate TLS on
#  their own listener, and both probe it with the openssl the runtime image already ships.
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
#    * No INSTALLED PACKAGE in the runtime stage, and in particular no SQLite package and no probe
#      tool. The readiness probe is built from what the base image already ships and the storage
#      engine arrives with the published assemblies; the runtime stage below reaches the network
#      never and runs apt never. Its own comment carries the full reasoning, because that is the
#      line at which a maintainer is most likely to add one.
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
# THE BUILD IMAGE IS PINNED TO A PATCH REVISION, NOT TO THE 10.0 FAMILY TAG, AND THE DIFFERENCE IS A
# SECURITY PROPERTY RATHER THAN A STYLE ONE. `mcr.microsoft.com/dotnet/sdk:10.0.400` is the SDK of the
# 2026-08-11 security release of the 10.0 channel (runtime 10.0.11, ten CVEs fixed against 10.0.10).
# The family tag `sdk:10.0` is the tempting choice, on the reasoning that a rebuild then picks a
# security patch up by itself. IT DOES NOT: a family tag that is already present locally is reused as
# it stands, so a builder holding a pre-patch `10.0` layer rebuilds on the vulnerable base and reports
# success. A patch revision cannot go stale silently - advancing it is a diff a reviewer sees - and CI
# additionally builds with `pull: true` so the reviewed digest is fetched rather than assumed
# (CWE-1104).
#
# Measured on this tag: DOTNET_SDK_VERSION=10.0.400 and `dotnet --version` = 10.0.400. global.json
# pins SDK 10.0.303 with rollForward: latestFeature, and 10.0.400 satisfies that pin by rolling
# forward one feature band within 10.0 - both are the same 10.0.11 platform release.
# --------------------------------------------------------------------------------------------------
#
# ⚠ PINNED BY DIGEST, NOT BY TAG, AND THE `pinned-tag:` LINE ABOVE THE `FROM` IS LOAD BEARING. A
# tag is a NAME a registry may republish over: `mcr.microsoft.com/dotnet/sdk:10.0.400` resolved to different content the
# day MCR rebuilt the OS layer, so a build that reported success could silently be building on content
# nobody reviewed (CWE-1104, CWE-494). The digest below is the content itself.
#
# THE COMMENT IS NOT DECORATION - THE CI IMAGE LEG PARSES IT. Dockerfile has no inline comment syntax,
# so the tag the digest was taken from is recorded on its own line immediately above. Before every
# build, .github/workflows/ci.yml reads that pair out of this file, asks the registry what the tag
# resolves to NOW, and fails the leg when it has moved - naming the new digest to adopt. Drift is
# therefore the signal that a patched base image exists, which is the OS-image freshness control.
# Both halves must stay together and stay adjacent: the digest alone cannot be re-checked, and the tag
# alone is not a pin.
# pinned-tag: mcr.microsoft.com/dotnet/sdk:10.0.400
FROM mcr.microsoft.com/dotnet/sdk@sha256:e1fc6e423f543119c406d24e2e687d67c569f18f04a37a8b0005d80ad0dcee80 AS build

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
# changes rather than against every edit to a .cs file. These SIX are the complete restore closure
# of the Persistence application: itself plus the five shared projects it references. The transitive
# edges among those five - Diagnostics references the shared kernel, and so does Eventful - resolve
# inside the same set, so nothing further is needed. No sibling service manifest appears, and none may:
# Persistence must restore, build and publish with no other service present.
#
# ⚠ THE EVENTFUL ENTRY WAS ADDED AFTER THE REFERENCE WAS, AND THE GAP BETWEEN THE TWO IS WHY THIS
# COMMENT NAMES A COUNT. PowerFramework.Persistence acquired a ProjectReference to
# PowerFramework.Shared.Eventful - it hosts the port of n_cst_threading_eventful, which the shared
# broker's own documentation names as its subclass - and this list was not updated with it. The HOST
# build stayed green, because the whole tree is present there; the CONTAINER build failed with CS0234
# "the type or namespace name 'Eventful' does not exist in the namespace 'PowerFramework.Shared'",
# because this file decides what the container can see. A reference added to the project file is a
# reference that must be added HERE TOO, in both lists below.
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
COPY shared/PowerFramework.Shared.Eventful/PowerFramework.Shared.Eventful.csproj \
     shared/PowerFramework.Shared.Eventful/

RUN dotnet restore services/persistence-service/PowerFramework.Persistence/PowerFramework.Persistence.csproj

# Now the sources. The five shared trees first and the application last, so an edit confined to the
# application does not invalidate the shared layers. Each COPY names one project directory: the
# .dockerignore keeps bin/, obj/ and TestResults/ out, so no host build output can leak in.
COPY shared/PowerFramework.Shared.Kernel/ shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Shared.Diagnostics/ shared/PowerFramework.Shared.Diagnostics/
COPY shared/PowerFramework.Shared.Containers/ shared/PowerFramework.Shared.Containers/
COPY shared/PowerFramework.Shared.Eventful/ shared/PowerFramework.Shared.Eventful/
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
# THE RUNTIME IMAGE IS PINNED TO A PATCH REVISION FOR THE SAME REASON THE BUILD IMAGE IS.
# `mcr.microsoft.com/dotnet/aspnet:10.0.11` is the runtime of the 2026-08-11 security release, matched
# to the Microsoft.AspNetCore.App version this refactor builds against and to the six platform package
# pins in Directory.Packages.props. Measured on this tag: ASPNET_VERSION=10.0.11 and
# DOTNET_VERSION=10.0.11 with APP_UID=1654 backed by a real `app` account, on Ubuntu 24.04 LTS, and it
# ships NEITHER curl NOR wget.
#
# NOT the sdk image, which carries a compiler, a package cache and a shell toolchain that a running
# service has no use for. NOT the non-ASP.NET `runtime` image either: this service hosts gRPC over
# Kestrel and would fail to start without the shared framework this image provides.
# --------------------------------------------------------------------------------------------------
#
# ⚠ PINNED BY DIGEST, NOT BY TAG, AND THE `pinned-tag:` LINE ABOVE THE `FROM` IS LOAD BEARING. A
# tag is a NAME a registry may republish over: `mcr.microsoft.com/dotnet/aspnet:10.0.11` resolved to different content the
# day MCR rebuilt the OS layer, so a build that reported success could silently be building on content
# nobody reviewed (CWE-1104, CWE-494). The digest below is the content itself.
#
# THE COMMENT IS NOT DECORATION - THE CI IMAGE LEG PARSES IT. Dockerfile has no inline comment syntax,
# so the tag the digest was taken from is recorded on its own line immediately above. Before every
# build, .github/workflows/ci.yml reads that pair out of this file, asks the registry what the tag
# resolves to NOW, and fails the leg when it has moved - naming the new digest to adopt. Drift is
# therefore the signal that a patched base image exists, which is the OS-image freshness control.
# Both halves must stay together and stay adjacent: the digest alone cannot be re-checked, and the tag
# alone is not a pin.
# pinned-tag: mcr.microsoft.com/dotnet/aspnet:10.0.11
FROM mcr.microsoft.com/dotnet/aspnet@sha256:207cc51496778557731c81ff670333d8ade4a4fec22768fd1be8e78474a84ecf AS final

# NOTHING IS INSTALLED IN THIS STAGE, AND THIS PARAGRAPH SAYS WHY. It sits here deliberately, because
# this is precisely the line at which a future maintainer reaches for a package manager.
#
# MEASURED ON mcr.microsoft.com/dotnet/aspnet:10.0.11 RATHER THAN ASSUMED. The image carries
# /usr/bin/openssl (3.0.13), /usr/bin/bash, /usr/bin/timeout and /usr/bin/head, and it carries NEITHER
# curl NOR wget. The readiness probe at the foot of this file is built from that set alone, so no
# package is required for it; the probe's own comment documents the pipeline clause by clause.
#
# INSTALLING curl FOR THE PROBE IS THE OBVIOUS MOVE AND IT IS REFUSED HERE. The base image is
# referenced by FAMILY tag on purpose, so that a security-patched rebuild picks the fix up - which
# means its package set advances. An apt version pinned against a moving base fails the build the moment
# the archive supersedes it, and an unpinned one is a floating dependency, which collides with the
# baseline this refactor holds itself to: no floating version, no wildcard and no "latest" anywhere. The
# Security definition refuses it for the identical reason. All four images share one probe idiom and
# install nothing, which also keeps a runtime package out of the one service in the system that holds a
# storage provider.
#
# NO SQLite PACKAGE, SPECIFICALLY - the tempting one, because this is the only service with a storage
# provider, and wrong twice over. The native engine is already placed beside the managed assemblies by
# the centrally pinned SQLitePCLRaw.bundle_e_sqlite3 reference, so an apt libsqlite3 would be redundant;
# and a second engine in the image risks the process binding one that parity was never measured against.
# Neither legacy sqlite3 binary is shipped either (constraint C-C), and no SQLCipher package appears
# anywhere: encrypted-database parity is out of Phase 1 scope.
#
# NOT AN ALPINE OR OTHER musl VARIANT EITHER. That bundle's native asset for this platform is linux-x64
# against glibc; swapping the tag for a musl base silently changes the ABI the engine needs and would
# demand a musl-targeted bundle to match it. The mandated tag stays.

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

# THE LISTENING PORT - ONE OF IT, DOCUMENTED HERE AND ESTABLISHED IN CONFIGURATION
#
# ASPNETCORE_HTTP_PORTS is cleared rather than set. appsettings.json declares ONE named Kestrel
# endpoint, carrying a protocol set a bare port number cannot express:
#
#     Rest  https://+:5101  Http1AndHttp2  - /health, /v1/ping AND the four published gRPC
#                                            contracts, C-05 through C-08
#
# BOTH VERSIONS ON ONE PORT, BECAUSE AAP 0.3.2.2 ASSIGNS THIS SERVICE ONE: "gRPC, plus REST /health
# and /v1/ping". The endpoint terminates TLS, which is what makes that work - ALPN negotiates between
# the versions per connection, measured on this toolchain rather than assumed. Binding a SECOND,
# HTTP/2-only listener would make a misaddressed client fail at negotiation rather than later, which is
# a real diagnostic gain and is still refused: it would publish C-05..C-08 at an address the plan does
# not assign, and the plan's port map is what the manifest, the probes and the e2e fixture all address.
#
# Leaving ASPNETCORE_HTTP_PORTS populated, including with the base image's own 8080, would add a
# SECOND, PLAINTEXT listener beside the configured one, so it is emptied. EXPOSE therefore DOCUMENTS
# the contract rather than establishing it. 5101 is this service's slot in the retained 5101-5105 band:
#
#     5101  Persistence     REST /health, /v1/ping AND its gRPC contracts over TLS - THIS IMAGE
#     5102  DataServices    REST projection for Gateway AND its gRPC contracts over TLS
#     5103  RESERVED - deliberately unallocated so the obvious Phase 2 slot stays visible rather than
#           being reassigned to one of the four services this phase builds. Nothing is provisioned
#           for it here: no stage, no variable, no build input.
#     5104  Security        REST + OpenAPI over TLS, and the system's SOLE token issuer
#     5105  Gateway         REST + OpenAPI over TLS, the documented composition-root access URL
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 5101

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
# /health is anonymous on all four services; /v1/ping requires a bearer token and answers 401 without
# one, so a probe must never point at it. Gateway reports healthy only once this service, DataServices
# and Security all do, so this probe is one of the three gates that readiness chain rests on. A compose
# manifest should INHERIT this declaration rather than write one of its own - a manifest-side probe
# written with curl would reintroduce the missing-tool problem for every one of the four images.
#
# BUILT FROM WHAT THE IMAGE ALREADY SHIPS: openssl performs the handshake and the request is written by
# hand. Nothing is installed and nothing is skipped, and this is the same pipeline the Gateway,
# DataServices and Security definitions use, so one idiom covers all four.
#
# A RAW /dev/tcp SOCKET IS NOT AN ALTERNATIVE HERE. This service's single listener terminates TLS, so a
# socket redirection writes plaintext bytes into a TLS endpoint: it never produces a status line, so it
# fails PERMANENTLY rather than intermittently. A probe that can only ever fail leaves a compose
# `depends_on: condition: service_healthy` gate shut for ever, which from the outside is
# indistinguishable from a service that never started.
#
# THE ANCHOR IS VERIFIED RATHER THAN SKIPPED, and that is the whole value of the probe. What it gates is
# not "did a socket answer" but the edge on which DataServices begins calling this service. A listener
# presenting a certificate the REST OF THE STACK rejects is not ready by that definition, and
# -noverify - curl's -k by another name - is exactly the flag that would hide it: the probe would go
# green while every real caller failed name or chain validation. docs/ARCHITECTURE.md 4.2 states the
# rule this follows. The anchor is read from INTERNAL_TLS_TRUSTED_CA_PATH with the documented mount
# point as its default, so no path and no trust material is baked into this image; an anchor that is not
# mounted fails the probe, which is the truthful answer for a deployment whose trust half was forgotten
# rather than an inconvenience to work around. -verify_return_error is what makes a verification failure
# FATAL: without it s_client prints the error and carries on, so the probe could still read a 200 from a
# listener whose chain it had just rejected. -verify_quiet beside it only silences the per-certificate
# chatter and changes no verdict.
#
# CLAUSE BY CLAUSE, because every one of them is load-bearing:
#   -quiet             implies -ign_eof. Without it s_client tears the connection down the instant
#                      stdin reaches EOF, which is BEFORE the response arrives; the probe then reads
#                      nothing and fails against a perfectly healthy service.
#   Connection: close  lets the server end the session, so the pipeline terminates on its own.
#   -verify_hostname   name validation is part of what is being checked, so the listener is addressed
#   and -servername    by a name its certificate has to cover. The certificate recipe in
#                      docs/ARCHITECTURE.md 9.3.1 carries DNS:localhost for exactly this, and
#                      connecting to 127.0.0.1 keeps the probe on the loopback interface with no DNS.
#   head -n 1          the status line and nothing else, matched on ' 200 ' WITH the surrounding spaces
#                      so a 200 appearing inside a reason phrase cannot pass. A refused connection, a
#                      failed handshake and a silent socket all fail.
#   timeout 5          bounds a hung handshake from the inside. The HEALTHCHECK --timeout below is set
#                      STRICTLY LARGER so that this guard, and not Docker's own kill, is what ends a
#                      hung attempt: equal values race, and Docker winning that race replaces a clean
#                      non-zero exit with a killed process.
#
# 5101 is written literally rather than read from a variable, because the port is fixed by the published
# contract - appsettings.json, docs/ARCHITECTURE.md and orchestration/.env.example all state it - and a
# variable only this probe would read is configuration surface nothing else consumes. A deployment that
# moves the listener through Kestrel's own configuration has to move this probe with it.
#
# THERE IS NO SECOND LISTENER TO PROBE. The single endpoint carries HTTP/1.1 and HTTP/2 together, so
# this HTTP/1.1 request and the gRPC contracts reach the same address and ALPN chooses between them; the
# host binds its endpoint or refuses to start, so 5101 answering already proves the process is serving.
#
# The start period is unhurried because this host does real work before it answers anything: it
# validates its bound configuration, refuses a storage directory that resolves inside the read-only
# legacy tree, creates that directory and proves it writable by writing and removing a probe file,
# resolves every runtime seam it executes SQL through, and loads its trust anchor. Failures inside that
# window do not count against the retry budget.
HEALTHCHECK --interval=15s --timeout=10s --start-period=30s --retries=5 \
  CMD ["/bin/bash", "-c", "line=$(printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' | timeout 5 openssl s_client -quiet -verify_quiet -verify_return_error -verify_hostname localhost -CAfile \"${INTERNAL_TLS_TRUSTED_CA_PATH:-/run/secrets/internal-tls/ca.crt}\" -servername localhost -connect 127.0.0.1:5101 2>/dev/null | head -n 1); case $line in *' 200 '*) exit 0 ;; *) exit 1 ;; esac"]

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
