# PowerFramework for PowerBuilder 

## document

- [文档](docs)
- [更新日志](logfile.md)

## license

BSD 2-Clause License

Copyright (c) 2013 - 2022, 金千枝(深圳)软件技术有限公司
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Copyright (c) 2013 - 2022
著作权由金千枝(深圳)软件技术有限公司所有。著作权人保留一切权利。

这份授权条款，在满足下列条件的前提下，允许使用者再发布经过或未经过
修改的、源代码或二进制形式的本软件：

1. 源代码的再发布，必须保留原来代码中的版权声明、这几条许可条件细目
   和下面的免责声明。
2. 二进制形式的再发布，必须在随同提供的文档和其它媒介中，复制原来的
   版权声明、这几条许可条件细目和下面的免责声明。
3. 所有使用到本软件功能的产品及宣传材料，都必须包还含下列之交待文字：
       “本产品内含有由金千枝(深圳)软件技术有限公司及其软件贡献者所开发的软件。”
4. 如果没有特殊的事前书面许可，原作者的组织名称，和贡献者名字，都不能
   用于支持或宣传从既有软件派生的产品。

免责声明：此软件由金千枝(深圳)软件技术有限公司和贡献者以“即此”方式提供，无论明示或
暗示的，包括但不限于间接的关于基于某种目的的适销性、实用性，在此皆明示
不予保证。在任何情况下，由于使用此软件造成的，直接、间接、连带、特别、
惩戒或因此而造成的损害（包括但不限于获得替代品及服务，无法使用，丢失数
据，损失盈利或业务中断），无论此类损害是如何造成的，基于何种责任推断，
是否属于合同范畴，严格赔偿责任或民事侵权行为（包括疏忽和其他原因），即
使预先被告知此类损害可能发生，金千枝(深圳)软件技术有限公司和贡献者均不承担任何责任。

<!-- Markdown lint policy for the .NET section appended below. Rationale is in docs/BUILD.md section 14:
     MD013 is 120 rather than the 80-character default and is disabled for tables and code blocks, because
     a wrapped command is a command that does not run. Prose IS wrapped, and to 120.

     THE THREE VIOLATIONS THIS FILE STILL REPORTS ARE PRE-EXISTING AND DELIBERATELY NOT FIXED: the trailing
     space on line 1, and the two asterisk bullets inside the BSD text on lines 18 and 21. Everything above
     this comment is the original framework introduction and the licence, it is legally load-bearing, and it
     is immutable, so it is not "corrected" here. Everything from this comment down is appended content. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

## PowerFramework on .NET 10

The PowerBuilder framework described above is unchanged. Everything under `ws_objects/` stays exactly where
it is and is **read-only**: 39 exported library folders, 544 objects and 160,445 lines of PowerScript that
together are the behavioural specification for the .NET work and the characterization oracle every parity
test is measured against. The .NET tree is **purely additive** — it replaces nothing, relocates nothing and
is not generated from the legacy sources. Both trees share one working directory, and only one of them is an
edit target.

This phase implements **four services of an eight-service target roster**: Gateway, DataServices,
Persistence and Security. The remaining four — DesignSystem, Documents, Integration and ScriptBridge — are
mapped object by object during discovery and deliberately **not built**; see
[Deferred capabilities](#deferred-capabilities-not-implemented-in-this-phase). No presentation surface is
created in this phase, so this is an API and service-level tree only.

### Services

Each service is its own project with its own solution file, container definition, settings and test project.
Nothing in the .NET tree depends on the PowerBuilder toolchain, the PowerBuilder runtime, or any of the
native binaries shipped beside it.

| Directory | Project | Port | Transport | Capability it owns |
| --- | --- | --- | --- | --- |
| `services/gateway-service` | `PowerFramework.Gateway` | **5105** | REST + OpenAPI | Sole ingress and composition root: request routing, capability gating, and the reserved extension points for the deferred capabilities |
| `services/dataservices-service` | `PowerFramework.DataServices` | **5102** | gRPC primary, plus a thin REST projection consumed only by Gateway | The DataWindow retrieve / validate / update triple, the 22-event DataWindow chain, and the column-expression engine |
| `services/persistence-service` | `PowerFramework.Persistence` | **5101** | gRPC, plus REST `/health` and `/v1/ping` | The only service that generates or executes SQL, and the only one holding a storage provider |
| `services/security-service` | `PowerFramework.Security` | **5104** | REST + `/.well-known/jwks.json` | The keyed cryptographic surface, and the **sole JWT issuer** for all four services |

- **Port `5103` is deliberately reserved** and is allocated to nothing in this phase.
- **Each service binds exactly one listener, on the port in the table.** The two services that carry gRPC
  contracts declare `Protocols: Http1AndHttp2` on theirs, so TLS application-protocol negotiation gives a
  readiness probe HTTP/1.1 and a gRPC channel HTTP/2 on that single port. The port in the table is
  therefore both the documented readiness address and the gRPC call address.
- **Every listener terminates TLS.** No service exposes a plaintext port — and here TLS is load-bearing
  rather than only prudent: on cleartext, `Http1AndHttp2` silently degrades to HTTP/1.1 alone, which would
  take every gRPC contract off the air while `/health` kept answering 200.
- Security is the only service that mints a token. The other three hold verification material only and
  validate against the key set Security publishes.

Service topology, the reasoning behind each transport choice and the full port map are in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md); the cross-service contracts are in
[`docs/CONTRACTS.md`](docs/CONTRACTS.md).

### Building

**Per service.** This is the whole command, and it is sufficient on its own. It is fenced as `text` rather
than `bash` because `<service-name>` is a placeholder, not shell syntax — a block that cannot be run
verbatim must not claim it can:

```text
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

Substituting a real directory name gives a block that **does** run verbatim:

```bash
set -euo pipefail
service=gateway-service   # or dataservices-service, persistence-service, security-service
cd "services/$service"
dotnet restore
dotnet build -c Release
dotnet test --collect:"XPlat Code Coverage"
```

`<service-name>` is one of `gateway-service`, `dataservices-service`, `persistence-service` or
`security-service`. Each service **builds and tests independently from a clean checkout**, with no reference
to any other service's project — the repository-root solution is not a prerequisite. It resolves because
each service directory holds exactly one solution file, so the bare `dotnet` commands find it automatically.

**Whole repository**, from the root. This is a developer convenience for building the whole tree in one
step; it is never the path by which a service's independence is demonstrated:

```bash
dotnet restore && dotnet build -c Release && dotnet test
```

Two toolchain facts that will otherwise cost a reader an afternoon:

- **.NET 10 emits `.slnx`, the XML solution format, and legacy `.slnf` solution filters are incompatible
  with it.** A hand-authored filter over a `.slnx` solution fails restore, build and test alike. The root
  solution is [`PowerFramework.slnx`](PowerFramework.slnx) and each service carries its own `.slnx`.
- **Bare `dotnet test` builds `Debug`**, even immediately after `dotnet build -c Release`. Where coverage
  has to be measured against the Release build, use `dotnet test -c Release --collect:"XPlat Code Coverage"`.
  The verbatim form above is kept alongside that variant, never replaced by it.

Build configuration lives in three files at the root: [`global.json`](global.json) pins the SDK,
[`Directory.Build.props`](Directory.Build.props) carries the settings every project inherits, and
[`Directory.Packages.props`](Directory.Packages.props) pins **every** package version centrally — so project
files carry versionless `PackageReference` entries only and no service can drift onto a different version.
Continuous integration runs the per-service command in four independent legs and gates each service on its
own coverage report against an 80% line-coverage floor.

Per-service specifics, the container build and the deviations from the attached environment's setup
instructions are in [`docs/BUILD.md`](docs/BUILD.md).

### Running locally

One manifest brings all four services up together. **The populated environment file is kept outside the
working tree**, and that is a requirement rather than a preference: a populated file written into
`orchestration/` names the absolute path of every file holding key material in this deployment and may carry
the operator identity's own secret as a value, and it is one `git add -A` away from being committed.
`--env-file` gives Compose the same file from anywhere on disk, so nothing is lost by keeping it out. The
committed ignore rules do exclude `orchestration/.env` for the case where it is kept in place anyway, but the
path above is still the documented one, because an ignore rule protects only the spelling it names.

From the repository root:

```bash
set -euo pipefail

PFW_ENV="${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env"

# 0700 on the directory is what actually protects the file; 0600 on the file is the second lock.
install -d -m 700 "$(dirname "$PFW_ENV")"

# NON-CLOBBERING, AND IT REPORTS WHICH BRANCH IT TOOK. Re-running this block after the file is populated
# must not destroy the key and three secrets in it: a plain `cp` would, silently, and the loss is
# unrecoverable because the signing key is stored nowhere else. The `if` also tells you whether your file
# was preserved, which a bare non-clobbering copy does not.
if [ -e "$PFW_ENV" ]; then
  printf 'Keeping the existing environment file at %s\n' "$PFW_ENV"
else
  cp orchestration/.env.example "$PFW_ENV"
  chmod 600 "$PFW_ENV"
  printf 'Created %s from the template - populate it before continuing.\n' "$PFW_ENV"
fi

# Populate the roster that file documents. TWELVE HOST PATHS are required for the bring-up and the
# manifest aborts BY NAME on any one that is unset, and on the path itself when it names a file that is
# not there: a certificate and a key PER SERVICE (eight), the one shared CA anchor
# INTERNAL_TLS_CA_PATH, SECURITY_JWT_SIGNING_KEY_PATH, and the two caller-secret paths
# SECURITY_CLIENT_SECRET_GATEWAY_PATH and SECURITY_CLIENT_SECRET_DATASERVICES_PATH.
#
# EVERY ONE OF THE TWELVE IS A PATH, NOT MATERIAL, and the value spellings are read nowhere: the
# manifest projects each file as a Compose secret and each service reads the projected path, so
# material pasted into this file configures nothing. The signing key is an RSA private key -
# `openssl rand` produces bytes the host rejects at startup, because it publishes an RSA-only key set.
#
# A thirteenth variable, SECURITY_CLIENT_SECRET, is the only one that carries a VALUE. It is the
# operator and end-to-end identity and is optional - `tests/e2e` needs it, the stack does not.
# docs/ARCHITECTURE.md section 9.3.1 generates the whole set in one block; orchestration/README.md carries
# the bring-up order. No value for any of them is committed anywhere in this tree.

docker compose -f orchestration/docker-compose.yml --env-file "$PFW_ENV" up --build -d
```

All twelve are **paths on your machine**, not paths inside a container: the manifest declares each as a
Compose secret and projects it read-only — the nine transport files under `/run/secrets/internal-tls/` and the
three non-TLS secrets under `/run/secrets/security/` — and each service is configured with those fixed
projected paths. **Seven of the twelve have to remain readable once projected** — the four server private
keys, the signing key and the two caller secrets — because Compose ignores `mode:`, `uid:` and `gid:` outside
Swarm and the images run unprivileged, which is why §9.3.1 generates those seven `0644` inside the `0700`
directory rather than `0600`. The three files nothing projects — the CA private key and the two caller
*certificate* keys — stay `0600`. Getting that split wrong does not degrade the stack: a `0600` server key
crash-loops one container, and a `0600` projected secret stops the bring-up outright.

The readiness contract, which is also what the container probes and the compose dependencies enforce:

- `/health` is **anonymous on all four services**.
- **Gateway reports healthy only after Persistence, DataServices and Security do.** The manifest expresses
  that with health-conditioned dependencies, so the composition root never comes up ahead of what it
  composes.
- `/v1/ping` **requires a JWT on all four services and returns `401` without one**. It is the standing proof
  that the authenticated-boundary requirement holds on every service and not merely at the ingress.
- The composition root is `https://localhost:5105`. Because every listener terminates TLS, probes are
  `https` and need the local trust anchor; a plaintext probe fails at the transport layer before any handler
  runs. That deviation from the environment's documented `http://` form is recorded in
  [`docs/BUILD.md`](docs/BUILD.md).

**A fresh stack needs no provisioning step.** Persistence applies its own migrations inside its startup
path — additively, idempotently, and before it can answer `/health` — so a brand-new `persistence-db`
volume reaches ready on its own and the health-conditioned chain opens in the ordinary way. A
characterization run that must not touch the schema between the two halves of a paired capture switches
that off with one setting; [`orchestration/README.md`](orchestration/README.md) carries the bring-up
order, the ordered readiness gates and that switch.

**What has actually been exercised is recorded in exactly one place** —
[`orchestration/README.md`](orchestration/README.md) §10 — and every other document in this repository,
including this one, defers to it rather than restating it. That is deliberate: an execution claim restated
in eight files is eight claims to keep true. The build, test and coverage figures behind it live in exactly
one place too — [`docs/BUILD.md`](docs/BUILD.md) §1.3, the canonical machine-readable verification record —
and are never restated here.

Cross-service workflow verification lives in [`tests/e2e/`](tests/e2e) and needs a running stack; its own
readme carries the install-and-run path. All key material reaches the services through configuration, so no
secret value is committed in the .NET tree; the hardcoded sites found in the legacy tree are inventoried,
with the action required for each, in [`docs/SECRETS.md`](docs/SECRETS.md).

### Deferred capabilities (not implemented in this phase)

Four capability areas are given a destination during discovery and deliberately left unbuilt. They have **no
project, no container image, no test project and no partial implementation** anywhere in this repository.
Each is represented on Gateway by a reserved route that answers **`501 Not Implemented`** with a
machine-readable body naming the deferred capability. A routing declaration is metadata about the shape of
the eventual system, not a stub of it:

| Reserved Gateway route | Deferred capability | What it will eventually own |
| --- | --- | --- |
| `/v1/design/**` | DesignSystem | UI, theming, geometry, colour, DPI conversion, canvas, painter, font and image handling, popup menus, tray icon and `win32` interop |
| `/v1/documents/**` | Documents | JSON, XML, ZIP, barcode and QR generation, file scanning, logging, and date and number conversion |
| `/v1/integration/**` | Integration | Outbound HTTP, FTP, WebSocket and MQTT, and the `pfwx.*` transports |
| `/v1/scripting/**` | ScriptBridge | Sciter, MiniBlink and WebView embedding, the PowerBuilder compiler and evaluator, and dynamic object and script invocation |

Three capabilities that in-scope logic genuinely touches are split rather than dropped: drop-down search,
context menu and column sort ship their headless half in DataServices — filter and sort expression
construction, the search state machine, and the menu item model with its computed logical widths — while
window positioning, DPI-to-pixel conversion, font measurement and rendering are deferred with DesignSystem.
Each gap is enumerated rather than left implied. The destination of every legacy object is recorded in
[`docs/DEFERRED.md`](docs/DEFERRED.md) and [`docs/SERVICE_MAPPING.md`](docs/SERVICE_MAPPING.md).

### Documentation

| Document | What it carries |
| --- | --- |
| [`docs/SERVICE_MAPPING.md`](docs/SERVICE_MAPPING.md) | The full 39-library, 544-object mapping to target services, in scope and deferred alike |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Service topology, the transport rationale per service, the port map, and capability gating |
| [`docs/CONTRACTS.md`](docs/CONTRACTS.md) | The ten cross-service contracts and the four reserved extension points |
| [`docs/BUILD.md`](docs/BUILD.md) | Per-service and whole-solution build commands, and the toolchain findings behind them |
| [`docs/PARITY.md`](docs/PARITY.md) | The characterization model, the legacy fixtures it is measured against, and the determinism seams |
| [`docs/SECRETS.md`](docs/SECRETS.md) | The hardcoded-secret inventory, each site with its severity and the action it requires |
| [`docs/DEFERRED.md`](docs/DEFERRED.md) | The four deferred services and the legacy objects assigned to each |

The five Chinese documents that were already under `docs/` — `README.md`, `Blink交互.md`, `Sciter交互.md`,
`PB多线程绕坑提示.md` and `n_cst_dwsvc_columnexp.md` — are the **legacy specification and are read-only**.
The documents above cite them as evidence; none of them is edited.

### Legacy tree

The legacy and .NET trees share one working directory, so the boundary is worth stating outright. These
paths are **read-only** — they are the behavioural oracle, never an edit target:

- `ws_objects/` — all 39 exported library folders and all 544 objects
- every `*.pbl`, `*.pbt`, `*.pbw`, `*.pbr` and `*.pbd`, at every level
- `oldversion/` and `pack/`
- the root native binaries: `pfw.dll`, `pfwx.dll`, `blink.dll`, `blinkfast.dll`, `sciter.dll`,
  `sqlite3.dll` and `sqlite3.cipher.dll`
- `res/`, `samples/` and `sciter_control/`
- `tests/blink/`, `tests/sciter/` and `tests/webview/`

**`tests/` is not a greenfield directory.** It already held those three legacy browser-asset directories,
and the .NET cross-service end-to-end suite was added beside them at [`tests/e2e/`](tests/e2e), purely
additively. Treating `tests/` as empty destroys oracle assets.

Two identifying facts, and no more than that: the most recent framework version recorded in `logfile.md` is
`3.0.7.2062`, and the application object declares the Appeon PowerBuilder 2021 runtime `21.0.0.1311`.
Neither is a specification — behaviour is derived from the sources themselves. The changelog stops years
before the end of the commit history, and the two PowerBuilder project objects in the legacy tree
contradict each other and both name a library that is not in the repository, so the legacy build is not
reproducible from them.

### Licence and attribution

The licence terms above are unchanged and apply to the whole repository, the .NET tree included: BSD
2-Clause together with the four additional conditions restated in Chinese in the `## license` section of
this file — among them the attribution sentence that has to appear in products **and** promotional
material, and the clause forbidding endorsement use of the organisation or contributor names without prior
written permission. They are not restated here; that section is the canonical text, and duplicating it
would invite the two copies to diverge.

Third-party attributions — the components vendored in the legacy tree and the packages the .NET services
use — are carried in [`NOTICE`](NOTICE) at the repository root, alongside a reproduction of the licence and
of the Chinese conditions. `NOTICE` is a notice rather than a licence: where anything in it could be read
differently from the licence texts, those texts govern.
