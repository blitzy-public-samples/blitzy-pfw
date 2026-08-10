# ==================================================================================================
#  services/security-service/Dockerfile - THE CONTAINER IMAGE FOR THE SECURITY SERVICE
#  ------------------------------------------------------------------------------------------------
#  ONE IMAGE, FOR ONE SERVICE. Security is the system's SOLE JWT ISSUER and the owner of the keyed
#  cryptographic surface, and this file produces its image and nothing else: two stages, exactly one
#  final image, no variant tag and no second final stage. Each of the three sibling services carries
#  its own Dockerfile; none of them is copied, restored or built here. Gateway, DataServices and
#  Persistence reach this service over the network and hold VERIFICATION material only - they never
#  mint a token, and nothing belonging to any of them is baked into this image.
#
#  WHY THE BASELINE MATTERS MORE IN THIS IMAGE THAN IN ANY OTHER
#  This is the only image in the decomposition whose compromise would yield SIGNING capability. It is
#  the only one that references Microsoft.IdentityModel.JsonWebTokens, because it is the only minter,
#  and at run time it is the only holder of the system's single signing input. The two hard rules
#  below are therefore load-bearing rather than ceremony: the final stage runs UNPRIVILEGED, and NO
#  CREDENTIAL OF ANY KIND EXISTS IN ANY LAYER OF THIS IMAGE.
#
#  LEGACY REFERENCE - READ FOR INTENT ONLY, NEVER TRANSLATED, NEVER SHIPPED
#  The capability this service replaces is a single PowerBuilder object declared
#  `global type n_crypto from nonvisualobject native "pfw.dll"`
#  [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8], publishing roughly sixty stateless overloads
#  [:L9-L73] over a CLOSED-SOURCE WINDOWS PE BINARY whose source exists nowhere in this repository.
#  That binary could not run in a Linux container even if shipping it were permitted, and the port
#  substitutes System.Security.Cryptography from the base class library instead. So this image
#  carries NO native legacy library: no COPY below names pfw.dll, pfwx.dll, sqlite3.dll,
#  sqlite3.cipher.dll, blink.dll, blinkfast.dll or sciter.dll, any *.pbl, *.pbd, *.pbt, *.pbw, *.pbr
#  or *.pbg file, or any path under ws_objects/, oldversion/, pack/, res/, samples/, sciter_control/
#  or the pre-existing legacy browser-harness directories under tests/. The repository-root
#  .dockerignore excludes every one of them from the build context, and every COPY below still names
#  an allowed path EXPLICITLY rather than relying on a blanket `COPY . .` - so what this image
#  contains is auditable from this file alone, and the read-only oracle stays read-only.
#
#  The other legacy borrowing is a POSTURE rather than code. The framework application object
#  initializes before anything else [ws_objects/pfw.pbl.src/pfw.sra:L91], pairs a finalize with it on
#  close [:L108], and on a structural fault decodes a seven-field assert payload and then ENDS THE
#  PROCESS [:L111-L144, reaching `HALT CLOSE` at :L143]. Two decisions at the foot of this file carry
#  that posture across: the entry point is EXEC FORM, so the runtime is PID 1 and receives SIGTERM
#  directly - which is what lets the host run its shutdown path and dispose the signing-key layer -
#  and nothing in this file softens a misconfiguration into a warning. A host that cannot import its
#  signing material refuses to start, deliberately, and this image does nothing to paper over that.
#
#  THE BUILD CONTEXT IS THE REPOSITORY ROOT, NOT THIS DIRECTORY
#  Build it as:  docker build -f services/security-service/Dockerfile .   (from the repository root)
#  Compose declares it as:  context: ..   dockerfile: services/security-service/Dockerfile
#
#  This is structural, not stylistic, and must not be "simplified". PowerFramework.Security.csproj
#  carries ProjectReference entries that rise three levels OUT of this directory into shared/ - the
#  published contracts project and the ported shared kernel - and MSBuild resolves
#  Directory.Build.props, Directory.Packages.props and global.json by walking UP from each project to
#  the repository root. A context scoped to services/security-service/ can see none of that and
#  restore fails. Narrowing the context looks tidier and does not work. Every source path in every
#  COPY below is therefore REPOSITORY-ROOT-RELATIVE.
#
#  THE ONE WAY THIS IMAGE DIFFERS FROM ITS THREE SIBLINGS
#  Its listener is TLS in every environment, so its readiness probe needs a TLS-capable tool and the
#  socket-redirection trick that suffices for a cleartext listener cannot work here. That is the only
#  reason a package is installed into the runtime stage at all; the full reasoning sits with the
#  instruction that does it, under READINESS PROBE in stage 2.
#
#  WHAT IS DELIBERATELY ABSENT FROM THIS FILE
#    * NO SECRET, IN ANY FORM. Not an ENV, not an ARG, not a default value, not a placeholder, and
#      not a value inside a comment. Three pieces of material reach this service and every one of
#      them arrives AT RUN TIME from the orchestration secret layer - the single signing input, the
#      server certificate together with its private key, and the certificate authority whose client
#      certificates the token endpoint trusts. This file reproduces none of their values and declares
#      none of them as a build argument either: a build argument is recorded in image history, so
#      declaring one would publish the exact material this service exists to protect.
#      orchestration/.env.example declares and documents those variables, and docs/SECRETS.md carries
#      the repository's credential register with locators and severities WITHOUT reproducing a single
#      value. Nothing from the eight legacy in-source credential sites catalogued there is
#      referenced, copied or reproduced here, and six of those sites sit inside regions the
#      repository-root .dockerignore excludes outright, so they cannot reach a layer even by
#      accident.
#    * No test project. PowerFramework.Security.Tests is the only producer of this service's
#      coverage.cobertura.xml and it runs in CI, not in the shipped image. security-service.slnx
#      enumerates it alongside the application, which is exactly why the build below targets the
#      APPLICATION .csproj directly rather than the solution: publishing the solution would drag the
#      test project, xunit.v3, the test SDK and the coverage collector into the image for nothing.
#    * No .slnf solution filter, anywhere, ever. Legacy solution filters are incompatible with the
#      .slnx XML solution format and fail during restore, during build and during test.
#    * No NuGet.config. This repository has none, and naming a path that does not exist fails the
#      COPY rather than being ignored.
#    * No pfw.i18n.xml. Stated explicitly BECAUSE THE SIBLING GATEWAY DEFINITION DOES COPY IT, and
#      copying it here for symmetry would be wrong. That file is a build input only for
#      PowerFramework.Shared.Localization, which declares a literal Content item naming it. This
#      service's restore closure is exactly three projects - the application, the contracts and the
#      kernel - and not one of them references Localization, so the file is not an input to this
#      build and no layer needs it.
#    * No second .dockerignore. The repository has exactly one, at the root, authored for this
#      context; a per-service copy would never be read for a root-context build and would be a
#      confusing dead file.
#    * No docs/, .github/, orchestration/, tests/e2e/ or characterization/ path. All are excluded
#      from the build context, so referencing any of them would break the build.
#    * Nothing for a deferred capability. DesignSystem, Documents, Integration and ScriptBridge have
#      no stage, layer, label, environment variable or build input here, and none of the placeholder
#      service names from the attached environment's five-directory roster is provisioned either.
#    * No second port. Exactly one is exposed - no debug port, no diagnostics listener, no
#      development-only surface, and no cleartext companion to the TLS endpoint.
#    * No extra tooling beyond the single probe dependency named in stage 2. No shell utility set, no
#      debugging agent, no telemetry sidecar and no interactive tooling is installed into either
#      stage.
#    * No performance, latency, throughput or availability claim. The repository publishes no such
#      budget, so none is asserted here and none is used to justify a choice below.
#
#  WHAT WAS EXERCISED, AND WHAT IS NOT CLAIMED - stated plainly rather than implied
#  Both base image tags below were inspected directly and report DOTNET_VERSION=10.0.10 and
#  ASPNET_VERSION=10.0.10 with APP_UID=1654 backed by a real `app` account; the runtime image ships
#  bash and openssl but NOT curl and NOT wget.
#
#  This definition was then BUILT and RUN rather than only reviewed. From the repository root,
#  `docker build --no-cache -f services/security-service/Dockerfile .` completed with zero warnings
#  and zero errors from restore and publish, with no dependency-audit finding, and uploaded a 3 MB
#  context out of a ~130 MB working tree - the measure of how much the repository-root .dockerignore
#  is doing. On the resulting image: the default user resolves to 1654(app) and `id -u` does not
#  return 0; the image declares exactly one exposed port; /app carries the publish output plus LICENSE
#  and NOTICE and no bin/, obj/ or test assembly; and no instruction in the image history and no
#  variable in the image configuration is credential-shaped. Running it with a throwaway certificate
#  and signing input supplied ONLY as run-time environment values: the host reported
#  `Hosting environment: Production` and `Now listening on: https://[::]:5104` - one endpoint, with no
#  address-override line - answered 200 on the anonymous /health, 200 on both /.well-known/
#  publications and 401 on /v1/ping without a bearer token, refused a cleartext request outright, and
#  the container reached Docker health status `healthy` on its first probe. PID 1 was the runtime
#  itself, and a SIGTERM shut the host down gracefully in about a second with exit code 0.
#
#  What is NOT claimed is a verified Compose bring-up of the whole stack: orchestration/
#  docker-compose.yml does not exist yet, so the multi-service readiness chain this image is meant to
#  participate in has not been exercised, and no such claim is made here.
# ==================================================================================================


# --------------------------------------------------------------------------------------------------
# STAGE 1 - BUILD
#
# mcr.microsoft.com/dotnet/sdk:10.0 is matched to global.json, which pins SDK 10.0.302 with
# rollForward confined to the latest feature band. The tag is the 10.0 FAMILY rather than a patch
# revision, which is deliberate: the pin that has to hold is the one in global.json, and a family tag
# lets a security-patched SDK be picked up on rebuild while still satisfying it.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# The repository-root build inputs first, because they change least often and everything below
# depends on all of them.
#
#   global.json                pins the SDK; found by MSBuild directory walk-up.
#   Directory.Build.props      net10.0, nullable, implicit usings, warnings-as-errors, the .NET
#                              analyzers, central package management, and the assembly copyright
#                              metadata that names LICENSE and NOTICE.
#   Directory.Packages.props   the single source of truth for every package version, including the
#                              two mandatory security pins that clear the dependency-audit
#                              advisories. EVERY PackageReference in this graph is versionless, so
#                              without this file restore fails outright rather than degrading.
#   .editorconfig              a BUILD INPUT here, not a preference file, and omitting it breaks the
#                              build rather than merely changing formatting. Warnings are errors and
#                              the .NET analyzers are on, while this refactor deliberately preserves
#                              the legacy SCREAMING_SNAKE constant spellings because those exact
#                              strings appear in serialized payloads, log records and
#                              characterization recordings. The naming-analyzer suppressions for them
#                              are FILE-SCOPED in .editorconfig, and three of those scoped files are
#                              in this very build graph: the kernel's RetCode.cs and Enums.cs, and
#                              this service's own Crypto/LegacyDefaults.cs.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./

# The project manifests ALONE, before any source file, so the restore layer is cached against
# dependency changes rather than invalidated by every edit to a .cs file.
#
# These three are the COMPLETE restore closure of the Security application: itself, the published
# contracts project, and the ported shared kernel. Verified rather than assumed - the contracts
# project declares no project reference of its own and the kernel declares neither a project nor a
# package reference, so the closure terminates here and nothing further is needed. No sibling
# service manifest appears, and none may: Security must restore, build and publish with no other
# service present, which is what independent deployability means in practice.
COPY services/security-service/PowerFramework.Security/PowerFramework.Security.csproj \
  services/security-service/PowerFramework.Security/
COPY shared/PowerFramework.Contracts/PowerFramework.Contracts.csproj \
  shared/PowerFramework.Contracts/
COPY shared/PowerFramework.Shared.Kernel/PowerFramework.Shared.Kernel.csproj \
  shared/PowerFramework.Shared.Kernel/

RUN dotnet restore services/security-service/PowerFramework.Security/PowerFramework.Security.csproj

# Now the sources: the two shared trees first and the application last, so an edit confined to the
# application does not invalidate the shared layers. Each COPY names ONE project directory, and the
# repository-root .dockerignore keeps bin/, obj/ and TestResults/ out of the context, so no host
# build output can leak in and defeat the in-container restore. The sibling test project directory is
# not named by any COPY here, so it is not in this image and cannot be.
COPY shared/PowerFramework.Shared.Kernel/ shared/PowerFramework.Shared.Kernel/
COPY shared/PowerFramework.Contracts/ shared/PowerFramework.Contracts/
COPY services/security-service/PowerFramework.Security/ services/security-service/PowerFramework.Security/

# Release configuration, and --no-restore on purpose: it makes an incomplete manifest layer above
# fail LOUDLY here rather than being papered over by an implicit restore that silently reaches the
# network again. Nothing is trimmed, single-filed, ready-to-run compiled or self-contained - none of
# that is required by this migration, and each would change what ships without a stated reason to.
RUN dotnet publish services/security-service/PowerFramework.Security/PowerFramework.Security.csproj \
  --configuration Release \
  --no-restore \
  --output /app/publish


# --------------------------------------------------------------------------------------------------
# STAGE 2 - RUNTIME (the one and only final image)
#
# mcr.microsoft.com/dotnet/aspnet:10.0, matched to the Microsoft.AspNetCore.App runtime this refactor
# builds against. Measured on this tag: ASPNET_VERSION=10.0.10 and DOTNET_VERSION=10.0.10, on Ubuntu
# 24.04 LTS.
#
# NOT the sdk image, which carries a compiler, a package cache and a build toolchain a running
# service has no use for and which would be pure attack surface in the one image that holds the
# signing key. NOT the non-ASP.NET `runtime` image either: this is an ASP.NET Core service and would
# fail to start without the shared framework this image provides.
# --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# --------------------------------------------------------------------------------------------------
# THE ONE PACKAGE INSTALLED INTO THIS IMAGE, AND WHY IT IS FUNCTIONAL RATHER THAN A CONVENIENCE
#
# This service binds ONE endpoint and it is TLS, in EVERY environment including Development:
# `https://+:5104` with ClientCertificateMode AllowCertificate, declared in the application's own
# appsettings.json and restated rather than relaxed in appsettings.Development.json. That is not
# hardening which could be dropped for a local run. `POST /v1/tokens` authenticates its caller with a
# CLIENT CERTIFICATE, which cannot be requested, presented or validated on a cleartext listener at
# all, and the key set and discovery documents this service publishes are the material the other
# three services trust - so a cleartext listener here would be a trust bootstrap that anyone on path
# could rewrite.
#
# The consequence for THIS file is that the readiness probe has to speak TLS. Socket redirection
# through the shell, which probes a cleartext listener with no added tooling, cannot negotiate a
# handshake - and this runtime image ships neither curl nor wget (measured, not assumed).
# docs/ARCHITECTURE.md 10.3 and docs/BUILD.md 7.1 both state the consequence in advance: a container
# health probe expressed as a shell command must install its own probe tool in the runtime stage, or
# the Compose `depends_on: condition: service_healthy` condition can never be satisfied and the
# readiness gate silently never opens. docs/ARCHITECTURE.md 4.2 fixes this service's gate as
# `curl -sf https://localhost:5104/health`, which is exactly what the HEALTHCHECK below performs.
#
# Kept as small as the requirement allows: one package, no recommended extras, and the package lists
# removed in the SAME layer so nothing is cached into the image. Installed BEFORE the USER switch
# below because apt-get needs root; everything after that line runs unprivileged.
#
# The apt version is deliberately not pinned, and that is a considered decision rather than an
# oversight. The base image is referenced by FAMILY tag so its package set advances with every
# security-patched rebuild; pinning one apt version against a moving base fails the build the moment
# the archive supersedes that version, which for the image holding the signing key would mean a
# security rebuild that cannot be produced. The reproducibility that matters is pinned where it can
# be: the SDK in global.json and every package version in Directory.Packages.props.
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
# restatement from the project readme and the eleven upstream attributions the framework depends on.
# Directory.Build.props already advertises both files in the assembly copyright metadata, so an image
# that omitted them would be pointing at material it does not carry.
COPY LICENSE NOTICE ./

# The published application, and nothing else: the application assembly, the two referenced project
# assemblies, the package assemblies, and the two settings files. Copied root-owned and
# world-readable, deliberately - the runtime user needs to READ and EXECUTE these files and never to
# write them, so it is given no ownership of them at all. Handing the service that holds the signing
# key write access to its own binaries would be a strictly weaker posture than this one.
#
# Only the publish output crosses the stage boundary, so no obj/ or bin/ intermediate and no part of
# the SDK reaches this image.
#
# appsettings.Development.json IS in that output, and that is correct rather than an oversight: the
# documented local bring-up selects the Development overlay, and that overlay exists precisely to
# restate this service's TLS listener rather than to relax it. It is never the EFFECTIVE configuration
# of this image by default - the environment pinned below selects Production, and only an explicit
# deployment override changes that.
COPY --from=build /app/publish ./

# THE EFFECTIVE ENVIRONMENT, AND THE LISTENING PORT
#
# ASPNETCORE_ENVIRONMENT is pinned to Production so that the shipped default is the production-safe
# overlay and the Development overlay cannot become the effective configuration of this image by
# accident. It stays overridable, which is what the documented local bring-up relies on:
# orchestration/.env.example sets Development for a local stack, and a Compose environment value wins
# over an image ENV.
#
# ASPNETCORE_HTTP_PORTS is set EMPTY, and this is the one instruction here whose purpose is not
# self-evident. The base image defines it as 8080 while this application defines its listener in
# configuration instead. Measured on the real application rather than reasoned about: with the
# inherited value in place the host logs "Overriding address(es) 'http://*:8080'. Binding to endpoints
# defined via IConfiguration and/or UseKestrel() instead" and binds the TLS endpoint ONLY, so the
# inherited value never produced a cleartext listener. Emptying it is belt and braces for the one
# image where a cleartext listener would matter most: it removes the inherited cleartext default
# STRUCTURALLY rather than relying on an override at startup, and it removes that override line from
# the log of every start. Re-measured with the value emptied: exactly one endpoint binds and /health
# still answers 200.
#
# The port itself comes from the application's own Kestrel endpoint configuration and not from here,
# because that endpoint carries a protocol set, a client-certificate mode and a TLS version list that
# a bare port number cannot express. EXPOSE therefore DOCUMENTS the contract rather than establishing
# it, and 5104 is this service's slot in the retained 5101-5105 band:
#
#     5101  Persistence     gRPC + REST /health and /v1/ping, TLS
#     5102  DataServices    gRPC primary + a thin REST projection for Gateway, TLS
#     5103  RESERVED - deliberately unallocated so the obvious Phase 2 slot stays visible rather than
#           being reassigned to one of the four services this phase builds. Nothing is provisioned for
#           it here: no stage, no variable, no build input.
#     5104  Security        REST + OpenAPI over TLS, and the system's SOLE token issuer - THIS IMAGE
#     5105  Gateway         REST + OpenAPI, the documented composition-root access URL
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 5104

# NON-ROOT, SWITCHED BEFORE THE PROBE AND BEFORE THE ENTRY POINT.
# APP_UID is published by the base image as 1654 and corresponds to the unprivileged `app` user and
# group that image already provisions, with a home directory that user can write. Using the variable
# the base image publishes rather than a hardcoded number is the mechanism provided for exactly this
# purpose and it cannot drift from the identity the image actually creates - measured on this tag: the
# resolved value is 1654 and `getent passwd 1654` resolves to app. Everything after this line, the
# health probe and the application alike, runs unprivileged; in the image that holds the signing key
# that is the difference between a contained fault and a root-owned one.
#
# The suppression below is scoped to this one rule on this one line and is stated rather than left as
# a standing finding. DL3066 warns that a non-numeric user id may be unresolvable on the host; a
# linter cannot see the base image's environment, so it reads the variable as non-numeric. On the
# built image the resolved value is numeric AND backed by a real account, so the rule's concern does
# not apply here.
# hadolint ignore=DL3066
USER $APP_UID

# READINESS PROBE - THE ANONYMOUS GET /health OVER TLS, AND NOTHING AUTHENTICATED
#
# /health is anonymous on all four services and answers 200 only for Healthy, 503 for Degraded and
# 503 for Unhealthy; --fail turns anything that is not a success status into a failed probe, which is
# what makes the Compose readiness gate mean something. /v1/ping is NEVER probed: it requires a
# bearer token and answers 401 without one BY DESIGN, so pointing a probe at it would mark a
# perfectly healthy container unhealthy forever.
#
# ClientCertificateMode is AllowCertificate rather than RequireCertificate precisely so this probe
# stays anonymous on the same listener that carries the certificate-authenticated issuance edge: a
# certificate is requested during the handshake but not demanded, so a probe presenting none still
# completes the handshake and receives its 200.
#
# --insecure IS CORRECT HERE AND IS NOT A RELAXATION OF THIS SERVICE'S TLS POSTURE. Two independent
# reasons, either one sufficient:
#   1. This request never leaves the container's own network namespace - it is a process asking
#      itself whether it is ready. There is no on-path party to authenticate, so validating a
#      certificate would establish nothing the loopback address has not established already.
#   2. The server certificate is supplied by the deployment at run time and its subject alternative
#      names are a deployment fact this file cannot know. A deployment whose certificate is issued for
#      the in-network service name - which is what the documented configuration uses - would fail
#      hostname verification against a loopback address, so a verifying probe would report a healthy
#      service as permanently unhealthy. That is the /v1/ping failure mode reached from a different
#      direction.
# Nothing about the listener changes as a result: every real caller still gets TLS, no consumer
# relaxes its own verification anywhere, and no trust anchor, certificate or key path is baked into
# this image. An operator probing from OUTSIDE the container, where there is a party to authenticate,
# uses the verifying form that docs/ARCHITECTURE.md records.
#
# 5104 is written literally rather than read from a variable. The port is fixed by the published
# contract - the OpenAPI document, docs/ARCHITECTURE.md and orchestration/.env.example all state it -
# and introducing a variable that only this probe would read means adding configuration surface
# nothing else consumes. A deployment that ever moves the listener through Kestrel's own
# configuration has to move this probe with it.
#
# The window is deliberately unhurried: this host validates its configuration and imports its signing
# material before it answers anything, and failures inside the start period do not count against the
# retry budget.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["curl", "--fail", "--silent", "--show-error", "--insecure", "--max-time", "4", \
    "--output", "/dev/null", "https://127.0.0.1:5104/health"]

# The framework-dependent application assembly, invoked by name. PowerFramework.Security.dll is fixed
# by the project file name, because PowerFramework.Security.csproj declares no AssemblyName override,
# so renaming the project without updating this line breaks the entry point.
#
# Exec form, so the runtime is PID 1 and receives SIGTERM directly instead of through a shell that
# would swallow it. That is what lets the host run its shutdown path, which disposes the signing-key
# layer - this service's structural equivalent of the finalize the legacy framework requires to be
# paired with its initialize [ws_objects/pfw.pbl.src/pfw.sra:L91 against :L108].
#
# No argument is passed and none is needed. Configuration reaches the host from appsettings.json, the
# environment-selected overlay and the environment itself, in that order, and a command-line value
# would be a fourth ingress for settings that already have one.
ENTRYPOINT ["dotnet", "PowerFramework.Security.dll"]

