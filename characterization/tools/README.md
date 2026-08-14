# `pfw-capture` — the target-side characterization capture driver

This directory holds the half of the characterization store that **runs**. Everything else under
[`characterization/`](../) is data: fifteen reviewed workflow definitions, a schema, a determinism mask
per workflow, and two recording roots. Until this driver landed there was nothing that could actually
take a capture, which meant the only route to a recording was to write one by hand — the one thing a
golden-master store must never accept.

## 1. What it does, in one paragraph

Given a workflow identifier, a running service and a **capture plan**, it drives every observable output
the workflow definition declares, screens every captured value for key material, proves that no supplied
secret survived, and writes normalized, diffable JSON artifacts plus a manifest. A divergence from a
declared expectation stops the run and **nothing is written**, because a partial recording cannot be told
apart from a capture of a broken service.

## 2. What it deliberately does not do

**It is not the legacy oracle and it cannot become one.** The legacy half of every pair needs the Appeon
PowerBuilder virtual machine. `pfw.dll` is a 32-bit PE that exports the PBNI entry points
(`_PBX_GetVersion@0`, `_PBX_CreateNonVisualObject@16`, `_PBX_CreateVisualObject@16`,
`_PBX_DrawVisualObject@12`, `_PBX_InvokeGlobalFunction@12`) and references `IPB_Session` throughout —
which is the session interface the VM *hands it*. The binary is therefore called **by** PowerBuilder and
cannot be invoked without it, and no `pbvm` library exists anywhere in this repository.

**So it refuses to land an unpaired capture into the store.** Writing into
`characterization/recordings/dotnet/<workflowId>/` while
`characterization/recordings/legacy/<workflowId>/` is empty would create exactly the state
[`../README.md` §3.2](../README.md#32-workflowid-is-the-pairing-key) forbids: a recording whose identifier
does not exist on the other side, which is not a partial comparison but no comparison at all. The driver
enforces that rule rather than documenting it. Capture to a directory of your own with `--output` while
the prerequisite is unmet — that proves the driver runs, and no parity conclusion may be drawn from it.

## 3. Reporting the pair state

Needs no service, no plan and no credential. It reads the store off disk.

```bash
dotnet run --project characterization/tools/PowerFramework.Characterization.Capture \
  -c Release --no-launch-profile -- --pair-state
```

Exit code **0** when no workflow holds exactly one half of its pair; **1** when one does. A workflow with
*neither* half is not a failure — it is exactly what its declared `executionStatus` says it is. CI runs
this as a gate, so the store's real state appears in every build log rather than having to be asked for.

## 4. Taking a capture

The stack must be up. [`../../orchestration/README.md`](../../orchestration/README.md) is the authority
for bringing it up; the services listen over HTTPS with certificates from a locally generated authority,
so `--ca-file` is required rather than optional.

```bash
dotnet run --project characterization/tools/PowerFramework.Characterization.Capture \
  -c Release --no-launch-profile -- \
  --workflow security-crypto-surface \
  --base-url https://localhost:5104 \
  --ca-file <path to the internal TLS authority certificate> \
  --issuer-url https://localhost:5104 \
  --client-id powerframework-dataservices \
  --client-secret-file <path to that caller's issuance credential> \
  --audience powerframework-security --scope security.crypto --scope ping \
  --key-ref <symmetric key reference> \
  --iv-ref <initialization-vector reference> \
  --file-ref <file reference> \
  --ref hmac=<keyed-hash key reference> \
  --ref passphrase=<short-passphrase reference> \
  --output <a directory outside the store>
```

`--dry-run` validates the plan and the destination, issues nothing and writes nothing. `--help` prints the
full option list.

### 4.1 A credential is never an argument value

`--token` and `--client-secret` **do not exist**, and the driver refuses them *by name* with an
explanation rather than as unknown options. Anything on a command line is readable from
`/proc/<pid>/cmdline` by any process of the same user for as long as the run lasts, so the driver takes a
**path** and reads the value itself. Only `--token-file` and `--client-secret-file` exist.

### 4.2 A reference is not key material

Every `--key-ref`, `--iv-ref`, `--file-ref` and `--ref name=value` value is an **opaque key-store
reference** the service resolves against its own configured store, which is what
[`docs/CONTRACTS.md` §5.2](../../docs/CONTRACTS.md) requires of every C-02 caller. That is why a reference
may be a command-line value where material may not. The driver still registers every supplied reference
with the screen, so a reference echoed back in a response body is rewritten to its placeholder rather
than recorded — QA finding F1 was exactly a `keyRef` reaching a log.

## 5. The plan is data, and that is the point

A workflow definition declares **what** must be observed; its `observedOutputs` are the store's reviewed
contract. It deliberately declares no endpoints, no request bodies and no chaining, because a definition
is reviewed as a specification and must not become a script. A plan under [`plans/`](plans) supplies
exactly that missing half, as JSON, with **one step per declared output**.

Because both are data, a guard test reads both and asserts their agreement **in both directions**:

- a declared output with no step would be silently uncaptured, and the recording would look complete
  while missing an observation the definition promised;
- a step naming an output the definition does not declare would put an **undeclared** fact into a
  recording — a comparison against something nobody reviewed.

`PowerFramework.Characterization.Capture.Tests/CapturePlanAgreementTests.cs` owns that assertion, along
with: every artifact name is one the store admits (checked with the driver's **own** predicate, not a
restatement), no plan carries anything shaped like key material, and every plan belongs to a workflow the
roster declares.

Plans exist only for workflows whose declared outputs are reachable over a **published REST contract**. A
workflow driven exclusively over gRPC has none, and the driver says so rather than inventing one.

### 5.1 The three capture modes, and why there are three

| Mode | Recorded | Chainable | Used for |
| --- | --- | --- | --- |
| `capture` | the value, verbatim | yes | a deterministic, recordable result — a digest, a recovered plaintext, a refusal's classification |
| `captureShape` | kind and size only | **no** | a member that carries or may carry key material. The absence of chaining is what stops such a member being threaded into a later request that echoes it |
| `captureOpaque` | kind and size only | yes | a value a later call cannot proceed without but that must never be recorded — a generated `keyRef`, a randomized ciphertext, a per-run signature. Registered with the screen, so any accidental appearance elsewhere is rewritten and then proved absent |

### 5.2 Derivations: a closed set of seven

Several declared outputs are not values a response carries. A round-trip verdict is a comparison. The
block repetition an unchained cipher mode leaks is a property of the ciphertext's structure. The character
classes a masked random generator draws from is a property of a sample **set**. Each is a small, total,
deterministic function, and the set is closed on purpose — an expression language in a plan file would
move behaviour into data, where no test reaches it.

`equals`, `hexOfBase64`, `byteLengthOfBase64`, `characterLength`, `hexBlockRepetition`,
`characterClasses`, `guidFormatting`.

Two of them — `hexOfBase64` and `hexBlockRepetition` — **reveal their input**, so they are refused over an
opaque value. Rendering an opaque value as hexadecimal would carry it into the artifact by a route the
screen cannot see, because the screen rewrites exact spellings rather than the same bytes in another
encoding. The other five answer with a boolean, a length, a set of character classes or a formatting
descriptor: summaries no one can invert, which is exactly what a masked seam's declared output asks for.

### 5.3 Teardown is per step

A step that generates an asymmetric key registers state in the service, and a capture that walked away
from it would leave a key registered for every run ever taken. Teardown is scoped to the **step** rather
than to the plan so it can name the thing it is releasing — the step's own chain is still in scope, so
`${call:generate:keyRef}` resolves. A plan-level teardown could only release something named on the
command line, which is to say something the run did not create.

## 6. What makes a recording trustworthy

- **Determinism by construction.** Member order is the plan's order. No clock is read, no duration is
  recorded, no host name and no run identifier is carried. `characterization/README.md` §6.4 forbids a
  performance measurement in this store and AAP §0.8.5 forbids asserting one anywhere.
- **Newline normalization.** UTF-8 without a byte-order mark, line feeds only. A mark or a carriage
  return would surface as a difference on the first line of every comparison — the platform artifact
  `../README.md` §3.7 requires be normalized rather than recorded.
- **Names the store admits.** The repository-root `.gitignore` carries *unanchored* patterns, so a
  recording named `capture.log` would be written, would look present locally, and would be silently
  untracked — a pair that exists on one machine and nowhere else. The driver refuses such a name and says
  the ignore rule is the reason.
- **Proof, not intent.** The whole artifact set is serialized, screened and *proved* free of every
  registered value before the first file is created, so a capture carrying a secret leaves nothing behind
  at all. A failure message never quotes the value it found: a build log is retained longer than a
  recording.

### 6.1 One measured non-determinism, and how it was handled

The first two runs of the crypto plan disagreed on a single member: whether a 24-character random string
under the digit, letter and symbol flags contained a digit. It did on one run and did not on the next.

That is not a flaky test — it is a **category error in the observation**. "Which classes does this
generator draw from" is a property of the generator; "which classes does this one string exhibit" is a
property of the sample. A class the alphabet does not contain can never be observed, so unioning across
samples only ever adds classes that are genuinely there, which makes the union sound where a single sample
is not. The plan now takes four samples and the derivation records the union **and** the per-sample
figures; seeing the per-sample figures disagree is the evidence that the union is the right comparison
rather than a convenience.

Three consecutive runs of the plan are byte-identical.

## 7. Building and testing

Two ordinary MSBuild projects, enumerated by the root `PowerFramework.slnx` — which is the **only**
solution that reaches the test project, and a test project no solution enumerates presents as a clean pass
while executing nothing.

```bash
dotnet test characterization/tools/PowerFramework.Characterization.Capture.Tests -c Release
```

The driver declares **no package reference at all**: `HttpClient` and `System.Text.Json` carry the whole
job, so it adds nothing to the dependency graph the CI vulnerability and deprecation gates walk
(AAP §0.5.3). Every test row runs offline — nothing starts a service, nothing mints a token, and nothing
writes into the store.
