<!-- Markdown lint policy for this file. Rationale is in docs/BUILD.md section 14. MD013 is 120 rather
     than the 80-character default, and is disabled for tables and code blocks: an evidence row carrying
     a legacy locator and a quoted finding cannot be wrapped without splitting the locator from what it
     proves, and a wrapped command is a command that does not run. Prose IS wrapped, and is held to the
     120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It applies to the seven documents this refactor authored and CANNOT reach the five read-only legacy
     Chinese documents in this folder, which are the behavioural oracle, are never edited, and carry
     pre-existing violations of their own (hard tabs, unlabelled code fences and others) that this
     refactor must not "fix". That unreachability is precisely why a per-file directive was chosen over a
     repository-root configuration artifact the plan does not provide for.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e,
     and no Markdown linter appears in it. A documented on-demand package-runner invocation would
     therefore instruct an unpinned version to be resolved and executed from the network outside that
     lockfile every time somebody followed the documentation, which the deterministic-automation baseline
     forbids. Lint with tooling that is already installed; the directive below is what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# PowerFramework → .NET 10 — Secrets Remediation Register

> ## No secret value is reproduced anywhere in this document, by design
>
> There is **no key, no certificate body, no password, no passphrase, no bearer token, no digest, no
> initialization vector, no symmetric key and no live endpoint address** anywhere below — not in
> whole, not in part, not PEM-wrapped, not as bare base64, not truncated, not partially masked, not
> as an "illustrative" value that resembles the real one. **A prefix is still material**, so no
> prefix appears either.
>
> This register records four things and nothing else: **where** the material is, **what class** of
> material it is, **how severe** it is, and **what must be done**. If you are looking for a value
> here, you will not find one, and that is the point. To assess a finding, open the cited locator.

This document is the **single register** of secret locators, severities and required actions for the
PowerFramework → .NET 10 decomposition, and it is also the **token-topology register** for the four
Phase-1 services. Its siblings deliberately do not restate any locator — they name only a *field* and
its handling rule and then point here — so completeness in this file is what keeps that division
honest. [`ARCHITECTURE.md`](ARCHITECTURE.md) §9, [`CONTRACTS.md`](CONTRACTS.md) §1.4 and §5.2,
[`DEFERRED.md`](DEFERRED.md) §4.5 and [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §12.2 each delegate
to it explicitly.

It discharges constraint **C-F** — nothing hardcoded may be carried forward, and the three secret
sites named in the requirements are **a floor, not a ceiling**.

## Current state of the artifacts this document references

**Every artifact referenced below is present in the tree**, including the complete `orchestration/` set —
[`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml),
[`../orchestration/.env.example`](../orchestration/.env.example) and
[`../orchestration/README.md`](../orchestration/README.md) — the solution and project files, the shared
libraries, the protocol and OpenAPI definitions under `shared/PowerFramework.Contracts/`, the per-service
settings, [`PARITY.md`](PARITY.md) and the read-only legacy tree.

**Present is not the same claim as exercised.** This document reports on secret *handling*, not on runs;
[`../orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
is the only execution-status statement in this repository, and where this document says an edge is
unexercised it is repeating that one rather than adding a second.

**One distinction in this document survives the manifest's arrival and must not be blurred by it.** The
manifest **injects paths**; it **mounts no material**. Every certificate, key and trust anchor named here
arrives by path from the deployment's own secret layer — the canonical default being `/run/secrets/`
([`orchestration/README.md`](../orchestration/README.md) §8.3) — and the manifest's only bind mount is the
`persistence-db` volume. That is a deliberate design property, not a gap: it is what keeps every piece of
key material out of the repository and out of every image layer (C-F). What it means for a reader is that
"the variable is wired" and "the file is present in the container" are two separate facts, and this
document is careful to say which one it is asserting.

---

## Table of contents

| Section | Contents |
| --- | --- |
| [1. Position, scope and evidence discipline](#1-position-scope-and-evidence-discipline) | The no-values rule, the absence of user rules, the locator convention, and the severity scale |
| [2. The sweep result](#2-the-sweep-result) | Three named, eleven found: the inventory, the two structural findings, the binary sites, adjacent defects, a corrected attribution, and the cleared false positives |
| [3. The remediation posture](#3-the-remediation-posture) | Never replicate, document, and rotate — never edit the legacy file; and the two operational follow-ups |
| [4. Token topology](#4-token-topology) | One signing secret, one issuer, three verifiers, every secret projected as a file rather than an environment variable (§4.1.3), the mutual-TLS fallback, and nothing scaffolded for a deferred service |
| [5. Credential-bearing fields on the new boundaries](#5-credential-bearing-fields-on-the-new-boundaries) | The per-field handling rules the contracts delegate here, and the deliberately ephemeral data-protection key ring |
| [6. Log hygiene — the two controls this refactor adds](#6-log-hygiene--the-two-controls-this-refactor-adds) | Why two controls are added rather than behaviours preserved, and why neither is a behavioural change: statement redaction (§6.1–§6.4) and caller-identifier neutralization (§6.5) |
| [7. Cryptographic weak defaults are preserved as annotated defaults](#7-cryptographic-weak-defaults-are-preserved-as-annotated-defaults) | Eight weaknesses kept exactly as they are — four of them established by absence — and annotated rather than fixed |
| [8. Constraint compliance and cross-references](#8-constraint-compliance-and-cross-references) | How this document honours each governing constraint, what it does not claim, and where to read next |

---

## 1. Position, scope and evidence discipline

### 1.1 What "no values" means in practice, and why the rule is drawn this wide

The rule is wider than "do not paste a key", because the near-misses are what actually leak material.
Each of the following is treated as a value and therefore never appears:

- any private key or certificate body, in whole or in part, whether wrapped in PEM block markers or
  carried as bare base64-encoded DER;
- any password, passphrase, user name paired with its credential, bearer token, digest,
  initialization vector, or symmetric key;
- any host address that identifies a live endpoint;
- any **truncated, elided or partially masked** rendering of the above — a first fragment of a key is
  still key material, and a masked password still discloses its length;
- any **"example" or "illustrative"** value shaped like the real one, because a reader cannot tell the
  two apart and will reasonably assume the real one was pasted.

Where the point being made needs the material characterised, this document describes its **shape**
instead — "a base64-encoded DER RSA private key", "a 32-character symmetric key", "a self-signed root
certificate authority" — and cites the locator so a reader with legitimate access can go and look.

**A note on how this document was checked, because it changes what an adequate check is.** The
certificate and key material in the MQTT test object carries **no PEM block markers at all**: direct
measurement of that file returns **zero** matches for a PEM header of any kind, while returning three
runs of more than two hundred contiguous base64 characters. Searching for PEM block markers is
therefore **necessary but not sufficient** — a document could pass that search while containing a
complete key. The decisive check is a scan for **any long unbroken base64-shaped run**, and this
document was verified against that scan as well as the marker search. Both checks are recorded in
§8.2 with their results.

### 1.2 No user-specified rules exist

The project's rules document was retrieved and it contains exactly one statement: **no user rules
were provided.** It is a single line, and re-verification returned the same result.

Three consequences, stated so that the absence of rules is not mistaken for latitude:

- **No rule is invented, inferred, or back-filled from convention.** Nothing in this register exists
  because a coding guideline demanded it.
- **Enterprise-standard best practice applies in the rules' place**, and for this document that bar
  is specific rather than vague: no secret in source, in application settings, or in any container
  definition; structured logging with redaction applied to the one field known to carry interpolated
  literal values (§6); and all key material injected through configuration rather than declared
  (§4.1).
- **Zero files enter scope because of a rule.** Every locator below is here because the sweep found
  material at it, and every control below is here because a governing constraint requires it.

### 1.3 Evidence discipline and the locator convention

Every claim in this document carries a locator, because the legacy tree is the only thing that can
adjudicate a behavioural or structural assertion in this refactor and nothing else is authoritative
([`ARCHITECTURE.md`](ARCHITECTURE.md) §1.2).

Locators are written as `path:L<line>` for a single line and `path:L<first>-L<last>` for a span. Every
locator in this document was verified twice: that the file exists, and that the cited line number
falls inside that file's actual length. **The verification was performed on file metadata and line
counts only — the content of the cited lines was never printed, echoed into a log, or copied into
this document.** That is a deliberate procedure: a register of secrets must be auditable without its
audit trail becoming a second copy of the secrets.

Where a claim rests on **absence** rather than presence — for instance that no key-derivation
function exists anywhere in the cryptographic constant set (§7) — the absence is stated as such and
the search that established it is described, because absence is weaker evidence than presence and
should not be presented as though it were the same thing.

### 1.4 The severity scale

Severities are only useful if they are comparable, so the scale is defined rather than left to
impression. A finding's severity is determined by **what an attacker holding the material could do
with it**, not by how alarming the material looks.

| Severity | Criterion |
| --- | --- |
| **Critical** | The material on its own grants an authenticated identity, or grants access to a system that is or appears to be live. No further discovery is needed to use it. |
| **High** | The material is complete and usable key material or a complete credential pair, but the system it authenticates to is not established as live, or its blast radius depends on how widely the containing object was adopted. |
| **Medium** | Trust material still within its validity period, or a secret whose exposure is broad but whose direct use requires access the material does not itself provide. |
| **Low** | Material that cannot currently authenticate anything — expired, superseded, or public by design — and whose significance is that it corroborates or elevates another finding. |
| **Informational** | Recorded for completeness and situational awareness. Not source-remediable by this refactor, or not itself a credential. |

Two consequences of applying the scale honestly are worth flagging up front, because both cut against
first impressions:

- **A certificate body is public material by design.** A certificate on its own therefore scores
  **Low**, even when it looks like the most alarming thing in the file. What makes a certificate
  serious is a *matching private key* sitting beside it — and that is exactly the pairing the sweep
  found (§2.4).
- **The most severe finding in this repository is not a key at all.** It is a credential triple in a
  comment block (§2.4, site 6), which scores **Critical** because it names a live-looking target
  along with an administrative account and its cleartext password — and because nothing in the
  repository can establish that the target is *not* still live.

---

## 2. The sweep result

### 2.1 The headline: three were named, eleven were found

The requirements named **three** hardcoded secrets and warned explicitly that they were **"a floor,
not a ceiling."** That warning was taken literally.

A repository-wide sweep — searching for certificate and key block markers, long base64-encoded DER
runs, and credential-shaped assignments — found **eight in-source sites plus three binary sites**.
That is **more than double** the three that were named.

| | Count |
| --- | --- |
| Sites named in the requirements | **3** |
| In-source sites found by the sweep | **8** |
| Of which newly found | **5** |
| Binary sites found by the sweep | **3** |
| **Total sites in this register** | **11** |

The distinction between the three named and the five newly found is not bookkeeping. It is the
evidence that the floor was treated as a floor, and it matters twice over because **both of the two
highest-severity findings in the repository are among the newly found five** (§2.4). A sweep that had
stopped at the three named sites would have missed a complete usable identity and a live-looking
administrative credential.

### 2.2 How the sweep was conducted

Three independent search families were run across the full repository depth, because each family
catches material the others miss:

1. **Block-marker search** — PEM-style block headers for private keys, public keys and certificates.
   This catches conventionally wrapped material. It found site 1 and **nothing else**, which is
   precisely why it was not relied upon alone.
2. **Long base64-run search** — any unbroken run of base64 alphabet characters long enough to carry
   DER-encoded key or certificate material. This is the family that found sites 2, 4, 5 and 8, none of
   which carries a block marker of any kind. Runs were length-banded rather than counted flat, because
   a moderate run is often just a long identifier in PowerScript source while a very long run is
   almost always encoded key material.
3. **Credential-shaped assignment search** — assignments whose target name denotes a credential and
   whose value is a literal. This found sites 3, 6 and 7.

Each candidate was then confirmed at its locator and classified by hand, which is how the false
positives in §2.10 were separated out rather than reported as findings. Match **counts and line
numbers** were the working output throughout; matched content was never printed.

### 2.3 The eight in-source sites

| # | Locator | Material class — no values | Severity | Named in requirements? | Required action |
| --- | --- | --- | --- | --- | --- |
| **1** | `tests/blink/test_jws.htm:L8-L22`, consumed at `:L23` | A plaintext PEM-wrapped **RSA private key**, used to sign a JSON Web Signature. The same region additionally carries a hardcoded bearer token value, a digest, a subject, and an expiry long since past | **High** | **Yes** | Never replicate. Treat the key as compromised; it is superseded by configuration-injected key material in Security (§4.6). Do not edit the file (§3.2) |
| **2** | `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L162` | A leaf **X.509 certificate**, carried as bare base64-encoded DER with no block marker. **Expired** — not-after 2021-08-20 | **Low** | **Yes** | Never replicate. Low on its own because a certificate is public by design and this one has lapsed — but see site 5, whose matching private key elevates the pair to Critical |
| **3** | `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L521-L522` | A plaintext broker **user name and password** pair, as two adjacent literals | **High** | **Yes** | Never replicate. Rotate the account at the broker. Supply any test credential through configuration, never through a literal |
| **4** | `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L161` | A self-signed **root certificate authority**, bare base64-encoded DER. **Still within validity — not-after 2027** | **Medium** | **No — newly found** | Never replicate. A trust anchor still inside its validity window should not be distributed in a source tree; confirm with its owner whether it remains a trusted root anywhere, and retire it if so. No private key for this root is present in the repository |
| **5** | `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L163` | A plaintext base64-encoded DER **RSA private key that matches the certificate at site 2** — certificate plus matching key is **a complete, usable identity**, not merely a certificate | **Critical** | **No — newly found** | Never replicate. Treat the identity as compromised and revoke it. Because the key is present, the expiry of site 2's certificate is not a mitigation for anything a holder of this key could reissue or impersonate offline |
| **6** | `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L176-L178`, inside the trailing comment block of the open event | A **broker host address, an account name whose spelling indicates administrative intent, and that account's cleartext password** — a production-shaped credential triple committed to version control. **Whether the host still resolves and the credential still authenticates cannot be determined from the repository** (see the note below the table) | **Critical** | **No — newly found** | **Operational, and it cannot be discharged by generating code.** Treat as compromised **until its owner confirms otherwise**, and rotate at the broker. See §3.6, follow-up 1 |
| **7** | `ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23`, consumed at `:L337`, `:L552`, `:L583`, `:L615` and `:L992` | A 32-character hardcoded **AES-256 configuration-encryption key** — the key under which the object encrypts and decrypts application settings | **High** | **No — newly found** | **Operational, and it cannot be discharged by generating code.** Rotate, *with* a migration for values already encrypted under it. The structural problem is explained in §2.5 and the follow-up in §3.6, follow-up 2 |
| **8** | `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466` and `:L718` | **Two distinct** plaintext base64-encoded DER **RSA private keys**, embedded directly in event scripts | **High** | **No — newly found** | Never replicate. Treat both keys as compromised. Three further weaknesses travel with them — see §2.6 |

Sites 2 through 6 are all in one object, the MQTT test window. Sites 1, 7 and 8 are in three further
objects. All eight lie in the repository's **test, demonstration or legacy-browser-asset** regions,
which is the fact that determines the whole remediation posture — see §3.1.

### 2.4 The two highest-severity findings, and why each scores Critical

Both are among the five the sweep newly found. Neither was named in the requirements.

**Site 5 — a complete usable identity.** The severity does not come from the key being a key. It comes
from the key being **the private counterpart of the certificate two lines above it**. A certificate
alone is public material and scores Low; a private key alone is serious but may authenticate to
nothing identifiable. The two together are an identity: anything that trusted that certificate can be
impersonated by whoever holds the key, and the holder can produce signatures that verify against a
certificate the repository also helpfully supplies. This is also why site 2's expiry is not a
mitigation — expiry constrains what a *validating peer* will accept today, not what a key holder can
do with the key material itself, and any relying party with relaxed or disabled validation is
unaffected by the expiry entirely. As §2.8 records, that relying party exists in the very same object.

**Site 6 — a live target with an administrative credential.** The severity comes from three
properties holding at once: the host is a **live** address rather than a placeholder, the account is
**administrative** rather than a limited test principal, and the password is **cleartext**. The triple
is directly actionable with no further discovery, which is the definition of Critical in §1.4. That it
sits inside a **comment block** rather than executable code lowers nothing: a comment is fully present
in the file, fully present in version-control history, and fully readable by anyone who can read the
repository. Comments are not a storage tier with different security properties, and treating them as
one is a common and expensive mistake.

Both of these are the reason §3.6 exists. Neither can be closed by writing code.

### 2.5 Site 7 is serious for a structural reason, not because of the literal

The 32-character key at `n_cst_appconfig.sru:L22-L23` would be an ordinary hardcoded-secret finding on
its own. What makes it materially worse is a combination of two structural properties, and the
combination is the finding:

**First, the key is effectively shared across every adopting deployment.** An override parameter does
exist — the widest entry points accept a caller-supplied key at `:L1006` and `:L1028` — but the one-
and two-argument entry points **pass an empty value**, and an empty value means the hardcoded literal
is retained. Since the shorter entry points are the ergonomic ones a caller reaches for, the practical
consequence is that **every deployment that adopts the object unchanged encrypts its settings under
one and the same key.** The exposure is therefore not scoped to this repository: it is scoped to every
installation derived from it.

**Second, there is no re-encryption path.** Nothing in the object re-encrypts stored values under a
new key. So rotating the key does not merely invalidate the old one — it **orphans every value already
encrypted under it**, which are then unreadable rather than merely stale. That is why the required
action in §2.3 is "rotate *with* a migration" rather than "rotate", and why this is an operational
follow-up (§3.6, follow-up 2) rather than something a code generator can close: the migration has to
read existing encrypted values with the old key and rewrite them with the new one, at each deployment,
before the old key can be withdrawn.

Neither property is visible from the literal alone, which is exactly why a sweep that only collects
literals produces a misleading severity ranking.

### 2.6 Site 8 carries three further weaknesses alongside the two keys

These travel with the keys in the same object, and a reader assessing that object needs all four
facts together rather than the keys in isolation:

| Locator | Weakness |
| --- | --- |
| `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L266` | An encryption key field is **defaulted to a plain alphabet string**, and the field is **not masked**, so the value is displayed as well as stored |
| `:L515` | An initialization vector field is defaulted the same way, likewise in an **unmasked** field |
| `:L449` | A **keyed-hash key is hardcoded** in the event script |

The unmasked-field detail matters more than it appears. A masked field at least signals to a user that
the value is sensitive; an unmasked field defaulted to a guessable string teaches the opposite lesson,
and a demonstration object teaches by example. The keys at `:L466` and `:L718` are the finding; these
three are why the object should not be used as a pattern by anyone reading it for guidance.

### 2.7 The three binary sites are vendored, and are not source-remediable

**The method, stated first so the counts below are reproducible.** Every root binary was scanned as
**bytes** — not as text, because these files are not text — for the PEM block header pattern
`-----BEGIN ([A-Z0-9 ]+)-----`. Each match was then classified by whether a corresponding
`-----END <same type>-----` exists within a plausible span *and* the span between them contains
base64 characters. That second step is what separates **embedded key or certificate material** from a
**format string that merely contains the marker text**, and it is the step that matters: the raw
marker count alone overstates the finding. Reproduce with:

```bash
python3 - <<'EOF'
import re, glob
pat = re.compile(rb'-----BEGIN ([A-Z0-9 ]+)-----')
for f in sorted(glob.glob('*.dll') + glob.glob('*.pbd')):
    data = open(f, 'rb').read()
    marks = list(pat.finditer(data))
    bodied = []
    for m in marks:
        t = m.group(1).decode()
        e = re.compile(('-----END %s-----' % t).encode()).search(data, m.end())
        if e and 40 < (e.start() - m.end()) < 20000            and len(re.findall(rb'[A-Za-z0-9+/=]', data[m.end():e.start()])) > 40:
            bodied.append(t)
    if marks:
        print(f, 'markers=', len(marks), 'with a real PEM body=', len(bodied), bodied)
EOF
```

| Locator | Markers | With a real PEM body | What the bodied matches actually are | Severity | Action |
| --- | ---: | ---: | --- | --- | --- |
| `sciter.dll` | 13 | **12** | 8 × `CERTIFICATE`, 3 × `RSA PRIVATE KEY` — **one of the three is itself encrypted**, carrying a `DES-EDE3-CBC` procedure header — and 1 × `DH PARAMETERS`. **The 13th marker has no body**: it is a `CERTIFICATE` marker inside a format string | **Informational** | Documented only. Not source-remediable |
| `blink.dll` | 1 | **0** | A `PUBLIC KEY` marker with **no body at all** — a format string used for public-key-pinning output | **Informational** | Documented only. Nothing embedded |
| `pfwx.dll` | 2 | **0** | Two `PRIVATE KEY` markers with **no body**, inside diagnostic message text | **Informational** | Documented only. Nothing embedded |

The remaining root binaries — `blinkfast.dll`, `pfw.dll`, `sqlite3.dll`, `sqlite3.cipher.dll` and
`pfw.pack.pbd` — return **zero markers**. Enumerating the clean ones matters as much as the dirty
ones: it shows the scan covered every binary rather than stopping at the first hit.

**Two of the three sites contain no embedded material whatsoever**, which raw marker counts do not
distinguish — counting markers alone gives 24, 1 and 1 without separating markers
from bodies, and the sciter figure is not reproducible that way by the method published above. The
narrower reading is the useful one: **the only binary with genuinely embedded key and
certificate material is `sciter.dll`**, and `blink.dll` and `pfwx.dll` are matches on marker *text*
in strings. Recording that distinction is the difference between an audit a reader can re-run and a
number they must take on trust.

> **What is asserted about site 6, and what is not.** The three values are read directly from the
> source, so their *shape* is a fact: a hostname, an account name whose spelling indicates
> administrative intent, and a cleartext password. Their *current status* is not a fact available here.
> **This document does not probe live systems** — resolving the host or attempting the credential would
> be both outside a documentation review and inappropriate — so it cannot state that the endpoint is
> reachable, that the account exists, or that the password still works.
>
> That uncertainty is precisely why the severity is **Critical rather than lower**. An unverifiable
> credential cannot be cleared from inside the repository, and the only safe default is to treat it as
> live until the owner establishes otherwise. Recording it as "confirmed live" would overstate the
> evidence; recording it as "probably stale" would understate the risk. The same reasoning applies to
> the certificate validity windows in the table: those *are* stated as facts because a not-after date
> is readable from the encoded certificate itself, whereas reachability is not readable from anything
> in the tree.

All three exist at the repository root and all three are **closed binaries with no source anywhere in
the repository**, so there is no literal to remove and no build step of this refactor that could
change their contents. They are recorded because a reader auditing the repository for embedded trust
material will find these matches and needs to know they were seen and classified, not missed.

Two points bound the exposure rather than dismiss it. First, embedded trust material in a browser or
transport engine is usually a bundled certificate-authority store, which is vendored trust
configuration rather than a secret — but that cannot be *verified* from here, which is why the
classification is Informational rather than "cleared". Second, and more usefully, **none of these
binaries enters any container image produced by this refactor**: they are excluded from the Docker
build context, individually and by extension, as §3.5 records with locators. The .NET tree does not
load them, link them, or ship them.

### 2.8 Adjacent security defects in the object holding sites 2 through 6

These are not secrets, so they are not sites. They are recorded because they are in the same object as
five of the eight findings and they materially change how that object's exposure should be read:

| Locator | Defect |
| --- | --- |
| `w_test_websocket_mqtt.srw:L154` | **Server-certificate validation is explicitly disabled** |
| `:L156` | **Host validation is explicitly disabled** |
| `:L153` | An inline comment immediately above states that the server certificate is not validated when using secure WebSockets — so the disabling is deliberate and documented, not accidental |
| `:L169` | A hardcoded endpoint |
| `:L530` | A hardcoded local proxy |
| `:L513` | A session-persistence filename |

The first three are the consequential ones, and they interact with site 5 in a way that is easy to
miss: a peer that does not validate the server certificate **cannot benefit from that certificate
having expired**. Expiry is enforced by the validator, and this object has switched the validator off.
So the mitigation a reader might reasonably infer from site 2's lapsed validity does not apply on this
code path.

### 2.9 A corrected attribution: the MQTT credential sites belong to the test library

An earlier statement of the estate mapping placed **five in-source secret sites in the
`pfwx.net.mqtt` transport library.** Direct measurement does not support that, and the correction is
recorded here rather than quietly applied because it changes where a reader would go to remediate.

Verified independently for this register: `ws_objects/pfwx.net.mqtt.pbl.src/` contains exactly three
objects — `nx_mqttclient.sru`, `nx_mqttconfig.sru` and `nx_mqttmessage.sru` — and **all three return
zero block markers, zero long base64 runs and zero credential-shaped assignments.** There is no
certificate, key or credential material of any kind in that library.

The MQTT-related credential sites are in the MQTT **test window**,
`ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw`, which is permanently out of scope and is
characterization-fixture source. Sites 2 through 6 in §2.3 carry the corrected locators.

The correction changes **no service assignment** — `pfwx.net.mqtt` is assigned to the deferred
Integration destination either way — but it does change which library carries credentials, and
therefore where remediation effort should and should not be directed. The same correction is recorded
from the deferral side in [`DEFERRED.md`](DEFERRED.md) §4.5 and from the mapping side in
[`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §12.2.

### 2.10 False positives, cleared so that remediation is not misdirected

This subsection carries as much weight as the findings. Misdirected remediation costs effort, produces
false confidence, and — because every candidate below sits inside the read-only legacy region — can
tempt an editor into breaching the read-only rule (§3.2) for no benefit at all.

| Candidate | Locator | Why it is not a site |
| --- | --- | --- |
| Placeholder client identifier, user name and password strings | `w_test_websocket_mqtt.srw:L501-L503`, **inside a documentation comment block spanning `:L490-L515`** | These are **usage examples in documentation**, not credentials. They authenticate to nothing. Note the asymmetry with site 6, which is *also* in a comment block but *is* a finding: what distinguishes them is that site 6's values are live and administrative while these are illustrative placeholders. The location did not decide either classification — the material did |
| An address-shaped literal | elsewhere in the test libraries | Reduces to a **version-string fragment**. Not an address at all |
| An address-shaped literal | in a device-information demonstration | A **well-known public DNS resolver**, used as a reachability target. Public by definition, and not a credential |
| Outbound URLs | across the test and demonstration libraries | **Public demonstration endpoints.** No embedded credential, no private host |

The general lesson is that neither "it is in a comment" nor "it looks like an address" settles
anything in either direction. Each candidate was resolved by asking what the material would let
somebody do, which is the same question the severity scale in §1.4 asks.

---

## 3. The remediation posture

This is the section most likely to be read wrongly, so it is stated bluntly and then justified.

### 3.1 Every one of the eight in-source sites lies inside the read-only region

This is the structural fact everything else follows from. All eight in-source sites are in the
repository's **test, demonstration, or legacy-browser-asset** regions:

| Region | Sites | Status in this refactor |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src/` — the test library | 2, 3, 4, 5, 6, 7 | Read-only. Permanently out of scope; characterization-fixture source |
| `ws_objects/pfw.demos.pbl.src/` — the demonstration library | 8 | Read-only. Permanently out of scope; characterization-fixture source |
| `tests/blink/` — legacy browser harness assets | 1 | Read-only legacy asset |

Every one of these capability areas maps to a **deferred** destination or to the permanently
out-of-scope set — none of them is implemented in this phase
([`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §9, [`DEFERRED.md`](DEFERRED.md) §3). And every one of
these paths is **read-only**: the legacy tree is never edited, deleted, moved, renamed or reformatted,
and that prohibition explicitly extends to the files that contain hardcoded secrets (**C-C**).

### 3.2 The posture

> ### Never replicate, document, and rotate — never "edit the legacy file."

Three obligations, in that order:

1. **Never replicate.** No .NET file produced by this refactor may contain any of these values, in any
   form or encoding. This is enforced structurally rather than by review: every secret reaches the
   .NET tree through configuration injection (§4.1), so there is no code path by which a literal could
   arrive.
2. **Document.** This register records every locator, its material class, its severity and its
   required action — without reproducing any value. Documentation is the durable remedy, because it
   survives after the people who found the material have moved on.
3. **Rotate.** Rotation happens **at the owning system** — the broker, the settings store, the
   certificate authority — not in this repository. Two rotations are operational follow-ups that code
   generation cannot discharge; they are in §3.6.

### 3.3 Why deleting the literals is the wrong instinct

A reader's first instinct on seeing this inventory is to go and delete the offending literals. That
instinct is wrong here, for two reasons that compound.

**It would corrupt the behavioural oracle.** The legacy tree is the *only* specification this refactor
has. There is no other statement of intended behaviour to consult, no usable legacy build definition,
and a changelog that stopped years before the commit history did. Every behavioural assertion in the
generated .NET code is adjudicated against a legacy locator, and the test and demonstration libraries
are specifically the **characterization-fixture corpus** — the recorded-oracle inputs that parity
testing compares against ([`docs/PARITY.md`](PARITY.md)). Editing an object in that corpus changes the
oracle, which means a subsequent parity failure can no longer be attributed: it might be a port defect,
or it might be the edit. That is an expensive and self-inflicted loss of diagnostic power.

**And it would buy nothing.** The material is already in version-control history. Deleting a literal
from the working tree removes it from the tip and leaves it in every prior commit, every clone and
every fetch. A reader who concluded "the secret is gone" would be wrong, and would now be wrong
*confidently* — which is worse than the original state, because the finding would no longer be visible
to prompt the rotation that actually fixes it.

So the two effective remedies are the ones in §3.2: **rotation at the owning system**, which
invalidates the material wherever it has spread, and **documentation**, which keeps the finding
visible until rotation is confirmed. Deletion from the working tree achieves neither and costs the
oracle.

This also settles a related question explicitly: **nothing in this document recommends editing,
deleting, moving, renaming or reformatting any legacy path.** That includes every `ws_objects/**`
object, every file under `tests/blink/`, `tests/sciter/` and `tests/webview/`, and the five
pre-existing Chinese reference documents in this same folder — `docs/README.md`,
`docs/Blink交互.md`, `docs/Sciter交互.md`, `docs/PB多线程绕坑提示.md` and
`docs/n_cst_dwsvc_columnexp.md`. This register is **purely additive**.

### 3.4 What the refactor commits to, concretely

- **No .NET file produced by this refactor contains any of these values in any form.**
- **This register records every locator, severity and required action, and reproduces no value** —
  which is also why its siblings name only a *field* and its handling rule and then point here.
- **Security's key material comes exclusively from configuration injection**, bound through the
  options pattern from the orchestration secret layer. No literal appears in source, in
  `appsettings.json`, in `appsettings.Development.json`, or in any container definition (§4.1).
- **Every credential-bearing field on a newly created boundary has an explicit handling rule**, and
  those rules are registered in §5 rather than left to the discretion of whoever implements the field.

### 3.5 A supporting control: the legacy regions are excluded from the container build context

This is **defence in depth, not the primary control.** The primary control is that no value is ever
replicated. But because each service's container is built with the **repository root** as its Docker
build context — necessary so that a service project can reach the shared libraries it references
([`ARCHITECTURE.md`](ARCHITECTURE.md) §10.2) — it is worth recording what keeps the legacy tree out of
the resulting image layers.

The repository-root `.dockerignore` excludes every region that holds secret material, verified by
locator:

| `.dockerignore` locator | Pattern | Sites it keeps out of every image layer |
| --- | --- | --- |
| `.dockerignore:L70` | `ws_objects/` | Sites **2, 3, 4, 5, 6, 7, 8** |
| `.dockerignore:L138` | `tests/blink/` | Site **1** |
| `.dockerignore:L139-L140` | `tests/sciter/`, `tests/webview/` | The remaining legacy browser assets |
| `.dockerignore:L111-L115` | The root native binaries individually, including `sciter.dll`, `blink.dll` and `pfwx.dll` | All three **binary** sites |
| `.dockerignore:L123` | `**/*.dll` | The same, by extension, at any depth |
| `.dockerignore:L71-L72` | `oldversion/`, `pack/` | The frozen legacy target and the consolidated redistribution libraries |

**All eight in-source sites and all three binary sites therefore sit inside excluded regions**, so none
of this material is present in any layer of any image this refactor produces. That is a stronger result
than the exclusions were designed for — they exist to keep a large read-only tree out of the build
context, and keeping the secret material out is a welcome consequence rather than their purpose.

The reason this is emphatically *not* the primary control is worth stating: a build-context exclusion
protects **images**. It does nothing about the repository, the version-control history, or any clone,
and it would silently stop protecting anything if a future change moved a service's build context. It
is a second line, and it is recorded as one.

### 3.6 Two follow-ups that code generation cannot discharge

These are **operational** actions. No amount of code generation resolves either one, and both will be
lost unless they are carried out of this document and assigned to an owner.

> #### Follow-up 1 — Rotate the broker credential triple at site 6, and treat it as compromised
>
> `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L176-L178` carries a broker host address, an
> account name indicating administrative intent, and that account's cleartext password. **Rotation must
> be performed by the owner of that broker**, who is also the only party able to establish whether the
> credential still authenticates. **Until that confirmation exists, the credential must be treated as
> live and the repository as disclosing it** — the safe default, not a verified fact. Revoking the
> identity at site 5 belongs to the same conversation, since both concern the same messaging estate.

The second follow-up has a different owner and a different shape: it is not a single account to reset
but a key whose blast radius extends to every deployment derived from this repository, and it cannot be
completed in one step.

> #### Follow-up 2 — Rotate the shared configuration-encryption key at site 7, *with* a migration
>
> `ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23` is a single key shared by every
> deployment that adopted the object unchanged (§2.5). Because the object provides **no re-encryption
> path**, rotation alone would orphan every value already encrypted under the old key. The action is
> therefore rotation **plus** a migration that decrypts existing values with the old key and re-encrypts
> them with the new one, per deployment, before the old key is withdrawn.

**The caveat that applies to both, and to every other site in this register:** material committed to
version control must be considered **exposed for as long as the history is retained**. It is present in
every clone and every fetch that has ever occurred. Rotation at the source system is therefore the only
effective remedy — it is the one action that makes the disclosed material worthless rather than merely
harder to find.

---

## 4. Token topology

The legacy opens no listening socket, registers no route and receives no unsolicited request — it is a
library loaded into a desktop process. Decomposition therefore creates the system's **first-ever**
ingress and several internal edges that never existed. "No new attack surface" cannot mean "no new
surface" literally, or it would forbid the refactor outright; it means **every newly created surface is
authenticated from the outset** (**C-G**). That reading is why Security is a Phase-1 service rather
than a later addition. [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.1 works through the argument.

### 4.1 Exactly one signing secret exists in the whole system

| | |
| --- | --- |
| **Name** | `SECURITY_JWT_SIGNING_KEY` — the **configuration key** Security reads. The orchestration template supplies it as `SECURITY_JWT_SIGNING_KEY_PATH`, a path to a file, which Compose projects and the service resolves through the `<KEY>_FILE` convention (§4.1.3) |
| **Held by** | Security, and no other component |
| **Kind of material** | An **RSA private key**, not a random symmetric secret. Security signs with `RS256` and publishes an RSA key set, so the two are not interchangeable: a random value has no modulus and no private exponent, cannot be imported as an RSA key, and cannot produce an `RS256` signature |
| **Format** | Base64 of the DER encoding of the PKCS#8 private-key structure, **on one line** — the shape that was required while the key travelled as an environment variable, since a dotenv file has no line continuation and a multi-line PEM block cannot be expressed in one. **Now that the key is projected as a FILE (§4.1.3) a multi-line PEM block is expressible too.** PEM is accepted either way, and is tried first — it was already the shape a secret store that can carry newlines would deliver: Security tries PEM first, both the PKCS#8 and the older PKCS#1 encodings, and falls back to base64-DER. Neither shape may be refused — legacy private-key material exists in both, the generator's PEM output being an optional fourth argument [`ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20`]. That acceptance order is **fixed code**, and the `Security:SigningKeyFormat` leaf naming it is additionally a bound, validated option — a value outside the recognised set is refused at startup. See §4.1.1 |
| **Supplied by** | Configuration injection from the orchestration secret layer, bound through the options pattern. **As a projected FILE rather than an environment variable** — see §4.1.3 for why that distinction is a security property rather than a packaging preference |
| **Appears in source?** | **No** |
| **Appears in `appsettings.json` or `appsettings.Development.json`?** | **No** |
| **Appears in any container definition?** | **No** |
| **Generated how?** | Locally, by the operator, at deployment time. It is not provided by the platform. The command is in `orchestration/.env.example` §1 |
| **Rotated how?** | **By an overlapped rollover — the procedure is §4.1.2, and §4.2.1 is what the other three boundaries do while you run it.** Publish the outgoing key beside the incoming one through `SECURITY_JWT_RETIRING_SIGNING_KEY`, wait out the overlap, then drop the retiring pair. A hard cutover (replace the value, restart once) is still available and still refuses the tokens already in flight |

The **name** of the variable is recorded here because consumers need to know what to set. **Its value
is not recorded here, is not recorded anywhere else in this repository, and no placeholder resembling a
value appears in this document** — an example key is indistinguishable from a real one to a reader, and
placeholder keys have a long history of reaching production unchanged.

The template `orchestration/.env.example` carries the variable roster with the one signing entry left
empty for exactly this reason: it tells an operator what to fill in without shipping anything to fill it
in with. **That file, the manifest that consumes it and the operator's guide beside it are all present in
the tree** — [`../orchestration/.env.example`](../orchestration/.env.example),
[`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) and
[`../orchestration/README.md`](../orchestration/README.md). All three are present.

#### What kind of key this is: RSA, not random bytes

`appsettings.json` sets `Security:SigningAlgorithm` to **RS256** and
`shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` publishes an **RSA-only** key set —
`kty` `RSA` with the modulus and exponent members, no symmetric member anywhere in the schema. RS256 signs
with an RSA private key, so random symmetric bytes cannot sign it and cannot be published as an RSA JWK.
**`openssl rand -base64 32` is therefore the wrong generator for this variable: what it produces is
incompatible with the published contract.**

| Property | Value |
| --- | --- |
| **Generation** | `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out security-signing.key` |
| **Accepted form** | The key **material itself**, as a value rather than a path. In the template that is the single-line base64-of-DER form, because the Compose dotenv format has no line continuation and a PEM block cannot be written there; a secret store that can carry newlines may instead supply PEM, which Security tries first. An earlier revision of this document described the variable as a path to a mounted PEM file — that is not what the template declares, and the two statements are reconciled here in favour of the template, which is the artifact an operator actually fills in |
| **Size** | **Measured AND enforced at 2048 bits — see §4.1.1.** `SigningKeyProvider` records the imported modulus as `SigningKeySizeBits` and refuses to construct below `SecurityOptions.MinimumSigningKeySizeBits` (2048); `SecurityOptionsValidator` reports the same floor at bring-up, so a short key fails configuration validation with the variable named and the material never echoed. The floor is a **constant, not a setting** — there is no leaf to raise it and none to lower it. The generation command above produces 2048 bits. The 1024-bit legacy allowance AAP §0.6.6.4 preserves applies to the **C-02 crypto surface**, where a caller supplies the size; it does not extend to this key, which is a boundary the decomposition created (§4.1.1) |
| **Public half** | **Derived, never configured.** Security computes the public JWK from the private key and publishes it under the `kid` in `Security:SigningKeyId`. There is no public-key variable, and there must not be one: two independently configured halves of one key pair is a way to publish material that does not verify what is being signed |
| **Rotation** | **Overlapped rollover, implemented — see §4.1.2 for the procedure and §4.2.1 for the convergence window.** Security holds an active key and, during a rollover, a **retiring** key supplied through `SECURITY_JWT_RETIRING_SIGNING_KEY` with its own `kid` in `Security:RetiringSigningKeyId`. Both are published in the key set; only the active one mints. So a replacement is a three-step rollover rather than a cutover: publish both, wait out the overlap, then drop the retiring pair. No code change, no rebuild, and no other service is reconfigured — the three verifiers pick up the second key from the published set they already fetch |

#### 4.1.1 What is enforced about this key, and why the 1024-bit legacy allowance does not reach it

This subsection exists because an overstated control is worse than a missing one: it is relied on. What
follows was read off the code rather than off the settings file.

> ⚠ **THE 2048-BIT FLOOR IS ENFORCED ON THE SIGNING KEY, AND THE SCOPE OF THE LEGACY ALLOWANCE IS WHY.**
> This has been decided twice in opposite directions, so the reasoning is recorded rather than the outcome
> alone. An intermediate revision withdrew the floor on the ground that AAP §0.6.6.4 requires each weak
> legacy cryptographic default to be preserved **as an annotated default** and names 1024-bit RSA as one
> that "remains a legal key size", and that §0.2.2.5 forbids correcting a legacy defect. That reading
> applied the allowance to the wrong surface. §0.6.6.4 governs the **C-02 legacy cryptographic surface** —
> the operations `n_crypto` actually publishes, where a CALLER supplies the key size and byte-for-byte
> parity with `GenRSAKey` is the obligation. That surface still accepts 1024 bits and still generates them,
> and this document's §6 annotates it.
>
> **JWT signing is not on that surface, because the legacy has no such surface.** The legacy framework has
> no token issuer, no JWT, no key set and no signing identity of any kind, and opens no listening socket at
> all [AAP §0.1.4] — so there is no legacy behaviour here either to preserve or to correct, and nothing for
> §0.2.2.5 to protect. What governs this key is AAP **G7** and constraint **C-G**: decomposition creates
> the estate's first ingress, and every newly created boundary is authenticated from the outset. A trust
> root all three verifiers validate every token against is that boundary's foundation. Importing a weakness
> from a surface that predates the boundary would not be parity; it would be a weakness with a citation.
>
> **The behaviour therefore changed with this correction, not only the description:** a 1024-bit signing key
> no longer starts the host. The floor lives in `SecurityOptions.MinimumSigningKeySizeBits` as a
> **constant** — anyone converting it back into a setting should read the note under the enforcement
> bullets below before doing so — and any edit to it must change this subsection,
> [`BUILD.md`](BUILD.md) §8, `orchestration/.env.example` and `orchestration/README.md` in the same pass.

**The accepted format is a validated setting over a fixed acceptance sequence.**
`Tokens/SigningKeyProvider.cs` always attempts the same closed sequence — PEM first, both the PKCS#8 and
the older PKCS#1 encodings, then base64 of the DER encoding — and no configuration reorders or extends it,
so the accepted set cannot drift. A value that is none of those shapes, `openssl rand` output being the
case that actually happens, **fails the import and the host refuses to start**, with a message that names
the variable and never echoes what was configured under it. Separately, `Security:SigningKeyFormat` **is**
a bound property of `SecurityOptions` and **is** validated: `SecurityOptionsValidator` refuses any value
outside the recognised set — which has exactly one member, `PemOrPkcs8Base64` — by name at startup. So the
leaf is not merely a record of the fixed behaviour: a deployment that names a format this service does not
implement (`Pkcs12` and `Jwk` being the plausible guesses) is told so at startup instead of having its
expectation silently ignored.

**THE KEY SIZE IS MEASURED AND ENFORCED, IN TWO PLACES, AND NEITHER IS CONFIGURABLE.**

- `SecurityOptionsValidator` imports the configured material, measures the modulus, and reports a
  validation failure naming the measured size and the 2048-bit minimum. Because the options are registered
  with `ValidateOnStart`, that failure is a refusal to start rather than a first-request error.
- `Tokens/SigningKeyProvider` measures again after its own import, records the size as
  `SigningKeySizeBits`, and throws rather than construct below the floor — logging one `Critical` record
  naming the measured size and the minimum. Neither the message nor the record echoes the material.
- The same floor applies to the RETIRING key, because a retiring key is still material every service in the
  estate is asked to trust. If the outgoing key is below the floor, that is the reason to rotate rather
  than a reason to publish it: cut over without an overlap and accept the window.
- There is **no minimum-size setting**, and `PowerFramework.Security.Tests` asserts by reflection that no
  configurable member has appeared which could LOWER it. A floor an operator can lower is not a floor, and
  the operator most likely to lower it is the one whose deployment already holds a short key.

**A 1024-bit RSA key therefore does not start this host.** The legacy allowance
`CRYPTO_RSA_BITS_1024` [`ws_objects/pfw.shared.pbl.src/enums.sru:L965`] is preserved where it belongs — on
the C-02 key-generation surface, where the caller supplies the size — and the divergence between the two is
asserted by a single test that exercises both halves, so neither can drift into the other.

**What still fails closed is unusable material, and that is not a size judgement.** A value that cannot be
imported as an RSA private key at all — `openssl rand` output being the case that actually happens — makes
the host refuse to start, reported as unusable and nothing else, with the variable named and the value
never echoed. The `/v1/crypto` surface's own weak defaults are annotated the same way and listed in §6.

#### 4.1.2 Rotating the signing key without refusing the tokens already in flight

**A rollover is implemented, and a hard cutover is no longer the only option.** Security holds an **active**
key and, for the duration of a rollover, a **retiring** one. The active key mints; both are published in
the key set under their own `kid`, so a verifier selects by the token header and never has to guess. That is
what the plural `keys` array in
[`security.v1.yaml`](../shared/PowerFramework.Contracts/OpenApi/security.v1.yaml)'s `JsonWebKeySet` now
carries in practice as well as in schema.

**Why a cutover was a real outage rather than a theoretical one.** The three verifiers cache the published
key set. Replacing the key in place removes the old `kid` in the same instant the new one appears, so every
token minted under the previous key is refused at every boundary until each cache has refreshed — an
estate-wide `401` that presents as a caller fault. Tokens live five minutes
(`Security:TokenLifetime`), which bounds the damage but does not prevent it.

**The three configuration inputs**, all supplied through the orchestration secret layer and none in any
settings file, container definition or source file:

| Input | Carries | Empty means |
| --- | --- | --- |
| `SECURITY_JWT_SIGNING_KEY` (supplied as `SECURITY_JWT_SIGNING_KEY_PATH` → projected file → `SECURITY_JWT_SIGNING_KEY_FILE`) | The ACTIVE private key. Mints every token | Refusal to start — there is no default for a secret |
| `SECURITY_JWT_RETIRING_SIGNING_KEY` (or, preferred, `SECURITY_JWT_RETIRING_SIGNING_KEY_FILE` → a projection the operator declares for the length of the rollover) | The outgoing private key. **Published, never used to mint** | No rollover in progress: the steady state |
| `SECURITY_JWT_RETIRING_SIGNING_KEY_ID` | The outgoing key's `kid`, bound at `Security:RetiringSigningKeyId` | As above; the two are a matched pair |

Plus `SECURITY_JWT_SIGNING_KEY_ID`, the active `kid`, which is an identifier rather than a secret — a `kid`
is published anonymously in the key set by design — and which exists as a variable so that the incoming key
can be given a NEW identifier without rebuilding the image.

**The procedure. Step 6 is the one that must not be shortened, and step 5 is the one that must not be
skipped.** [`../orchestration/README.md`](../orchestration/README.md) §8.1.1 carries the operator-facing form
of the same steps, including the override the preferred retiring form needs.

1. Generate the incoming key **into a new file beside the current one**, so the outgoing key stays readable
   for the overlap: `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out <new file>`. Give it
   the same permissions every projected file gets.
2. Give Security the OUTGOING key as verification material — through
   `SECURITY_JWT_RETIRING_SIGNING_KEY_FILE` and a projection declared for the length of the rollover, or as a
   value in `SECURITY_JWT_RETIRING_SIGNING_KEY` — and put the current `kid` in
   `SECURITY_JWT_RETIRING_SIGNING_KEY_ID`.
3. **Repoint `SECURITY_JWT_SIGNING_KEY_PATH` at the file from step 1**, and put a **new, different** `kid` in
   `SECURITY_JWT_SIGNING_KEY_ID`. The path is what rotates the active key: nothing reads a
   `SECURITY_JWT_SIGNING_KEY` **value**, so material written there rotates nothing while still publishing a
   second identifier — the outgoing key would go on minting under the new name.
4. Restart Security only. Both keys are now published; new tokens carry the new `kid`; tokens minted under
   the old one keep verifying.
5. **Confirm two keys AND two distinct moduli.** `.keys | length` is not sufficient on its own: a no-op
   rotation publishes two identifiers over one modulus and satisfies it completely, so compare
   `[.keys[].n] | unique | length` as well. Both must be `2`.
6. **Wait out the overlap** — at least `Security:TokenLifetime` (five minutes as shipped) **plus** the
   verifiers' clock skew (30 seconds) **plus** however long a verifier's cached key set may remain stale.
   Nothing enforces this wait, which is precisely why it is written down here.
7. Clear the retiring pair and restart Security again. One key is published, carrying the new identifier; the
   rollover is complete, and the retired material can be destroyed at its source.

**What the host refuses, so a half-applied rollover cannot run silently.** Material with no identifier (a
key set entry cannot exist without a `kid`); an identifier with no material (publishes nothing while reading
as a rollover in progress); two keys sharing one identifier (an ambiguous key set in which a verifier may
select the wrong key and report what looks like forgery); and a retiring key below the 2048-bit floor. Each
is a startup failure naming the configuration key and never the material.

**Nothing in this repository is replaced.** A rollover edits the operator's own environment file and the
files it names on the operator's own host — or the objects in whatever secret store supplies them — and
touches no file here, of which there is none carrying key material in any form.

**The transport identity is a separate set of files.** `POST /v1/tokens` authenticates its caller with
either a shared secret presented as an HTTP `Basic` credential or a client certificate — **either
satisfies it** (§4.3) — so where the certificate scheme is used Security additionally needs a server
certificate and a client-CA to trust.
The **server** certificate is not Security-specific and not three-of-four: **all four services** terminate
TLS, each with its OWN material, supplied through `<SERVICE>_TLS_CERTIFICATE_PATH` and
`<SERVICE>_TLS_CERTIFICATE_KEY_PATH`, which the Compose manifest projects into that service alone as
`Kestrel:Certificates:Default:Path` and `:KeyPath`. One pair therefore covers **all four bound ports** —
5101, 5102, 5104 and 5105, one listener per service. **Because one certificate is presented under four different service
names and is probed locally at a fifth, it must carry every one of them as a subject alternative name —
`security-service`, `dataservices-service`, `persistence-service`, `gateway-service`, `localhost` and
`127.0.0.1` — or be replaced by one certificate per service.** Gateway is the easy omission because
nothing else in the system calls it; an end-to-end suite and an operator both reach it by name.
A common-name-only certificate cannot authenticate the
other names, and every TLS client in the system rejects it for them; the SAN-bearing command set is in
[`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1 and is the canonical copy. What is Security-specific is the
trust anchor it validates presented client certificates against, `SECURITY_MTLS_CLIENT_CA_PATH`, and the
client certificate each calling service presents — `GATEWAY_MTLS_CERT_PATH` / `GATEWAY_MTLS_KEY_PATH` and
`DATASERVICES_MTLS_CERT_PATH` / `DATASERVICES_MTLS_KEY_PATH`. All are paths, all mounted from the
orchestration secret layer, and none is material. **`SECURITY_MTLS_CERT_PATH` and
`SECURITY_MTLS_KEY_PATH` are not part of this roster and must not be reintroduced as service settings**
(the end-to-end suite reads the same two names for its own CLIENT pair, which is a different consumer and
is not affected by this rule): they would belong to a
second mutual-TLS listener that does not exist, and the server certificate comes from the shared
`TLS_CERTIFICATE_*` pair above.

> **The trust anchor is READ by application code; what is still missing is the mount.**
> `SECURITY_MTLS_CLIENT_CA_PATH` is consumed by an ASP.NET Core configuration key after all —
> `Security:MutualTls:ClientCaPath`, loaded at startup by `CallerCertificateTrust.Load` in Security's
> composition root and installed as Kestrel's `ClientCertificateValidation` callback, which builds a
> presented caller certificate's chain under `X509ChainTrustMode.CustomRootTrust` against that anchor
> alone. It feeds a **second** consumer as well, and that second one is why a correct certificate used to
> be refused: `Security:ClientCertificateAuthorityPath` is the ISSUANCE anchor, read by
> `Tokens/ClientCertificateTrust` so a certificate that completed the handshake may establish an
> identity, and no variable was ever published for it — so the documented bootstrap completed the
> handshake and then answered `401 E_ACCESS_DENIED` on every certificate while `Basic` callers kept
> minting. Security's composition root now adopts the listener anchor as the issuance anchor when the
> issuance key is unset, so this one variable is sufficient and an explicitly configured issuance anchor
> still wins. That is the second of the two implementations that were open, and it is why
> `services/security-service/Dockerfile` deliberately **installs no trust anchor**: no `ca-certificates`
> step and no `update-ca-certificates`, because the OS trust store is not the decider and the runtime
> stage needs no root-privileged step. A configured-but-unreadable anchor is a refusal to start.
> `AllowAnyClientCertificate` is called nowhere in the repository.
>
> **What is not operable is a projection of *this* anchor.**
> [`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) projects the three
> server-side TLS files as read-only secrets and deliberately projects nothing for the five `*_MTLS_*`
> variables, injecting them verbatim as **container** paths — mutual TLS is the per-pair fallback the brief
> declines to scaffold, and a *host* path set there refuses startup, measured rather than predicted. With
> the path unset the callback defers to the platform's own verdict. **The certificate alternative on the
> issuance edge is therefore unexercised end to end**, and §4.3 repeats the point at its own point of use.
> That is a gap in ONE of the two accepted schemes: `POST /v1/tokens` also accepts a shared secret as an
> HTTP `Basic` credential, which is what the documented bring-up supplies and what it exercised, so no
> statement here should be read as "issuance is unauthenticated".

[`BUILD.md`](BUILD.md) §8 and `orchestration/.env.example` §1 restate the variable roster, and the three
must agree word for word.

> #### ⚠ Where the filled-in environment file must live — read before generating a key
>
> **This repository's ignore rules do not exclude an environment file, and this refactor does not
> change them.** A generated signing key written to a path inside the working tree is therefore an
> untracked file that `git add -A` would stage and a careless commit would publish. Nothing in the
> tooling prevents that, so the control has to be the path itself.
>
> **The documented path is an environment file kept OUTSIDE the working tree**, referenced explicitly.
> Every line below runs against the tree as it stands, and the copy is **guarded** so a second run cannot
> overwrite a key already written into it:
>
> ```bash
> set -euo pipefail
> PFW_ENV="${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env"
> install -d -m 700 "$(dirname "$PFW_ENV")"
> if [ -e "$PFW_ENV" ]; then
>   echo "Keeping the existing environment file at $PFW_ENV"
> else
>   cp orchestration/.env.example "$PFW_ENV"
>   chmod 600 "$PFW_ENV"
> fi
> # write the key to a file and point SECURITY_JWT_SIGNING_KEY_PATH at it -- it is an RSA PRIVATE key,
> # not random bytes:
> #   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform DER | base64 -w0
> # THE KEY FILE IS 0644 INSIDE THAT 0700 DIRECTORY, NOT 0600, because the manifest PROJECTS it and
> # Compose ignores `mode:`, `uid:` and `gid:` outside Swarm -- at 0600 security-service cannot read it
> # and refuses to start. The same applies to the two caller issuance credential files. The 0600 above
> # is the ENVIRONMENT file, which is never projected. docs/ARCHITECTURE.md section 9.3.1 part 4b
> # decides every mode in one place.
> cd orchestration && docker compose --env-file "$PFW_ENV" up --build -d
> ```
>
> **The shape of that material is part of the secret handling rule, not an implementation detail.**
> Security signs with `RS256` over a closed `RS256`/`RS384`/`RS512` allow-list and imports the value as
> an RSA private key, so symmetric random bytes are rejected at startup and the host refuses to start.
> Two consequences follow for this register. First, the correct remedy is to generate an asymmetric key,
> never to relax the algorithm to an HMAC family — the JWK set is anonymous verification material, so an
> HMAC key there would publish the signing secret and make all three verifiers co-signers, which is the
> sole-issuer property in §4.2 gone. Second, a PEM private key is a **multi-line** artifact and an
> environment file holds one line per value, which is why the documented form is a single-line base64 of
> the DER encoding; where a secret store can carry newlines, the PEM form is accepted unchanged.
>
> The attached environment's own instruction is the in-tree form, `cp .env.example .env` followed by
> `docker compose --env-file .env up`. It remains supported and
> [`../orchestration/README.md`](../orchestration/README.md) §3.1 records it —
> **but only with the precondition stated there**: add an ignore rule covering `orchestration/.env`
> *before* writing any key into it, and never rely on the file being untracked to keep it unpublished.
> An untracked secret is one `git add -A` away from a tracked one.
>
> Either way, `.env` files are excluded from every image layer by the repository-root `.dockerignore`,
> so no environment file reaches a container image. That control is about images, not about version
> control, and it is not a substitute for the path discipline above.

#### 4.1.3 Every secret is projected as a file, not passed as an environment variable

**The signing key and both caller credentials reach their services as projected files.** The orchestration
template names a *path* for each — `SECURITY_JWT_SIGNING_KEY_PATH`,
`SECURITY_CLIENT_SECRET_GATEWAY_PATH`, `SECURITY_CLIENT_SECRET_DATASERVICES_PATH` — the manifest declares
each as a Compose secret and projects it read-only under `/run/secrets/security/`, and each service reads it
through a `<KEY>_FILE` companion of the configuration key it already used.

**This replaced an environment variable, and the change is a security property rather than a packaging
preference.** An environment variable carrying key material is readable from far more places than its owner
expects:

- `docker compose config` renders it in cleartext — and that is precisely the command an operator is told
  to run when a bring-up misbehaves, so the natural debugging step printed the RSA private key that signs
  every token in the estate;
- `docker inspect <container>` returns the whole environment to anyone who can reach the daemon socket, and
  so does the API behind it;
- `/proc/<pid>/environ` exposes it to any process in the container running as the same account;
- every child process inherits the block whether it needs it or not;
- it lands in shell history, in CI logs that echo the environment, and in a crash dump.

A projected file has none of those properties: one path, one readable account, absent from a rendered
manifest, an inspect payload and every process environment.

**Three rules, and each refuses rather than guessing.** They are enforced in each service's own
`Configuration/FileBackedSecrets.cs` — one copy per service, because no behaviour crosses a service
boundary in this estate:

| Condition | Outcome | Why not the alternative |
| --- | --- | --- |
| `<KEY>_FILE` set and readable | Its **trimmed** contents become `<KEY>` | The trim matters: every ordinary way of writing a secret to a file appends a newline, and an untrimmed base64 key fails to decode at first use |
| Both `<KEY>` and `<KEY>_FILE` carry a value | **Refuse to start** | Two sources for one secret means nobody can tell which credential is being presented, so a rotation applied to one appears effective while the other is still in use |
| `<KEY>_FILE` set but missing, unreadable or empty | **Refuse to start** | Falling back to the environment value would substitute a *different* credential for the one the deployment declared — silent, and indistinguishable from success |

**The environment-variable form still works**, so a deployment that has not migrated is not broken (C-I).
What changed is what the shipped manifest *does*.

**Two optional slots keep a value form, and the reason is structural.** A Compose secret's `file:` must name
a path that already exists, and both the retiring signing key and the optional operator identity are *empty*
in the steady state — so declaring a projection for either would abort every ordinary bring-up to serve a
case that is not happening. Both carry a `_FILE` companion that is passed through, and that companion is the
**preferred** form whenever the slot is actually in use: during a rollover the value form would put a signing
key back into the environment for the length of the rollover, which is the exposure the active key was moved
out of.

### 4.1a Three issuance-roster secrets, and they are a different kind of material

The signing secret above is the only **signing** secret in the system, and that remains exactly true. It
is not, however, the only *required* secret — the issuance edge authenticates its callers with a shared
secret per caller, and a secrets register that omitted three required secrets would be incomplete in the
one direction that matters.

| | |
| --- | --- |
| **Names** | `SECURITY_CLIENT_SECRET_GATEWAY`, `SECURITY_CLIENT_SECRET_DATASERVICES`, `SECURITY_CLIENT_SECRET` |
| **Held by** | Security holds all three (it verifies them); Gateway, DataServices and the end-to-end suite each hold **only their own** (they present it) |
| **Kind of material** | A random shared secret — 32 bytes, base64. Compared byte for byte with `CryptographicOperations.FixedTimeEquals` and **imported by nothing**, which is the precise inverse of the signing key: `openssl rand -base64 32` is the right tool here and the wrong one there, and `openssl genpkey` is the right tool there and the wrong one here |
| **Format** | Opaque. Any non-blank value is accepted, because a shared secret has no structure to check. It travels as the password half of an HTTP `Basic` credential, so it is UTF-8 and may contain a colon (the reader splits on the first colon, per RFC 7617) but must not contain a newline — an environment file holds one line per value |
| **Named, not stored** | Each roster entry declares `SecretConfigurationKey` — the **name** of a flat configuration key. The material arrives through configuration injection from the orchestration secret layer. That is the same indirection `Security:KeyStore` uses for caller-referenced material, and it is what keeps the roster reviewable in a settings file at all (C-F) |
| **Absence is fail-fast** | Security resolves every secret its roster names at startup and **refuses the host** when a named key resolves to nothing or to whitespace, reporting the roster **position** and never a value. The alternative is a caller that mysteriously cannot authenticate against a service whose readiness probe reports healthy |
| **Rotation** | Per caller and independently — rotating one does not invalidate a token already minted, because a roster secret authenticates the *request for* a token and is not the material any token is signed with. That is a deliberate property: rotating the signing key invalidates every token in flight, rotating a roster secret invalidates nothing |

**Why three and not five.** Gateway obtains tokens addressed to DataServices; DataServices obtains them
addressed to Persistence and to Security's cryptographic surface. **Persistence has no roster entry and
needs no secret** — it requests no token at all and reads Security's published key set anonymously, so
provisioning one would create material nothing consumes and nobody rotates. The third is the operator /
end-to-end identity `pfw-e2e-suite`, registered only in `appsettings.Development.json` because a suite
identity is a local bring-up fact rather than a deployment one; `tests/e2e` presents it as
`SECURITY_CLIENT_ID` and `SECURITY_CLIENT_SECRET`, and the identifier must be set to that exact subject
because the issuance edge reconciles the claimed subject against the authenticated credential identity
ordinally.

**Do not share one value across two callers.** Sharing collapses two identities into one credential, and
the permission model then grants each caller the other's audiences and scopes in practice while the roster
says otherwise — a divergence between configuration and behaviour that nothing in the system reports,
because from Security's point of view nothing is wrong.

**What the secret does *not* buy.** Authenticating at the issuance edge establishes *which* caller is
asking; it grants nothing by itself. What that caller may obtain is decided by its own roster entry —
the audiences it may address and the scopes it may request — and a request outside either is refused
rather than narrowed. A leaked roster secret therefore yields exactly that caller's permissions and no
more, which is what makes least privilege here worth stating.

### 4.2 One issuer, three verifiers

| Role | Held by | What it means |
| --- | --- | --- |
| **Issuer** | **Security only** | The only component in the system that can mint a token |
| **Verifier** | Gateway, DataServices, Persistence | Hold **verification material only**. No signing key, no minting capability |

- **Security mints** short-lived service tokens on request, and **publishes verification material** at
  the standard JSON Web Key Set path together with OpenID discovery metadata. Both publication
  endpoints are anonymous, because verification material is public by design — that is what
  distinguishes it from a signing key.
- **The other three services verify only.** They validate inbound tokens with the framework's **stock
  bearer handler**, configured to fetch the published key set. None of them can mint a token and none
  holds a signing key.
- **Any per-service signing-key names, if retained at all, are verification-side names and are not
  independent signing authorities.** A service that could mint its own tokens would be a second issuer,
  and the security properties of a sole-issuer topology depend on there being exactly one.

There is a specific security reason this arrangement is preferred, and it is the reason Security speaks
REST rather than gRPC: a stock bearer handler consumes a published key set and discovery document with
**zero bespoke code**. The security-critical validation path — signature checking, key selection by `kid`,
issuer and audience validation, clock-skew handling — is therefore **framework code rather than
hand-written code**, and it remained framework code when the key ring arrived on the issuing side: a
verifier that fetches a two-key set selects by `kid` with no change to any of the three services (§4.1.2).
Choosing gRPC for Security would have forced custom key-set retrieval into three separate
services, which is a net *increase* in hand-written security-critical code and precisely the wrong
direction.

`/v1/ping` is the standing proof of the property: it requires a token on all four services and returns
`401` without one, so authentication is testable rather than merely asserted
([`ARCHITECTURE.md`](ARCHITECTURE.md) §4.2).

#### 4.2.1 Rotating the signing key: what each boundary does, and the order to do it in

**The issuer half can now be overlapped (§4.1.2); the verifier half is still a cache expiry, and the two
are not simultaneous.** This subsection records the measured behaviour of a HARD CUTOVER — replace the key,
restart once, publish one key — because that is the case the intervals below actually bound, and because it
is what a rollover that skips step 5 of §4.1.2 degenerates into. It was written after a finding: a cutover
left the estate accepting the **retired** credential and refusing the **current** one at the same time,
and neither the window nor a safe order was written down anywhere. Run the overlapped procedure and the
first row of the measurement table below stops applying, because no token is ever signed by a key the
published set omits.

**What each boundary does the moment the issuer's key changes**

| Boundary | Where its verification key comes from | What it does at the instant of rotation |
| --- | --- | --- |
| **Security** | **In process**, from its own signing-key layer. It configures no bearer `Authority` and no `MetadataAddress` at all, so it builds no configuration manager and performs no metadata retrieval | Converges **immediately** on restart. It is both issuer and verifier of its own tokens, and both halves move together |
| Gateway, DataServices, Persistence | The **published key set**, fetched by the stock bearer handler and **cached** | Keep serving from the cached set. A **pre-rotation** token still verifies; a **post-rotation** token is refused `401` with `IDX10503`, naming a `kid` that matched nothing |

**The two intervals that bound it, and why both are needed**

Each verifier now configures both of the token library's retrieval intervals — `MetadataRefreshInterval`
and `MetadataAutomaticRefreshInterval`, declared in each service's settings file and validated at startup
— because they bound the two halves of the window and the library's defaults for them are five minutes
and **twelve hours**:

- **`MetadataRefreshInterval` (shipped: 5 seconds)** is the floor before a refresh that a FAILED
  validation asked for is actually performed. `RefreshOnIssuerKeyNotFound` is left at its default of
  true, so an unknown `kid` requests a refresh at once — this interval is what decides how soon that
  request is honoured, and therefore how long a **current** token stays refused.
- **`MetadataAutomaticRefreshInterval` (shipped: 5 minutes, the library's own minimum)** is the
  background interval. A **successful** validation provokes no refresh at all, so nothing else ever drops
  a **retired** key: this interval alone decides how long a superseded credential keeps working.

**The third duration, which neither of those bounds**

A superseded key set does not stop being acceptable when it stops being *current*.
`BaseConfigurationManager` keeps a **cache of recently-good configurations** and the token handler retries
against them when validation fails against the current one, so a retired key stays usable for
`LastKnownGoodLifetime` — **one hour by default**. This was measured rather than inferred: with both
intervals above configured, a token signed by a retired key was **still accepted nine and a half minutes
after the rotation**, past a background refresh that had already replaced the current configuration, and
two separately retired identities were both still honoured.

Each verifier therefore **bounds that lifetime to its own background interval**, derived from it rather
than made a third setting, so a superseded set is gone after one refresh cycle. It is bounded rather than
**switched off**: `UseLastKnownGoodConfiguration` stays at its default of `true`, because that fallback is
what keeps a boundary validating tokens through a transient inability to *fetch* the key set. Turning it
off would trade a bounded acceptance window for a hard authentication failure during any metadata outage.

**What was measured, on the shipped settings and on the library's defaults**

| Rotation half | With the library's defaults | With the shipped settings |
| --- | --- | --- |
| A **newly minted** token is accepted again | **232 s** | **1.1 s** |
| A token signed by the **retired** key stops being accepted | still accepted at **570 s**, bounded only by the one-hour fallback lifetime | **293 s**, bounded by the five-minute fallback lifetime |

Two properties of the library shape those numbers and are worth knowing before reading a rotation
timeline:

- **The first refresh request in a process's life is always honoured immediately**, whatever
  `MetadataRefreshInterval` says. The 232 s figure above is the *second* rotation in one process, which is
  the case an operator actually meets; a boundary's very first rotation converges at once and is not
  representative.
- **`MetadataAutomaticRefreshInterval` cannot go below five minutes** — that is the library's own floor —
  so five minutes is the tightest retired-key bound available without switching the fallback off.

Neither number is a service-level commitment: no latency budget or availability target is published
anywhere in this system (AAP §0.8.5), and these are measurements of one deployment's convergence taken to
establish that the window is bounded and roughly how tightly.

Both intervals are a **bound on the cutover window**, and the other way to remove that window is the one
§4.1.2 now implements: the issuer publishes the superseded key beside the new one until every consumer has
converged, which a key *set* carries natively. The two mechanisms are complementary rather than
alternatives — the intervals bound how long a verifier can be wrong about the current set, and the overlap
means being briefly wrong costs nothing.

**Why the retiring key does not breach the sole-issuer rule, stated plainly because AAP §0.6.6.3 counts
signing secrets.** That clause fixes ONE signing secret in the estate and forbids the other three services
holding an independent signing authority; what it protects against is a second ISSUER. The retiring key is
not one: it never mints — `TokenIssuer` signs with the active credential only — it is published as
verification material exactly like the active key's public half, it is held by the same sole issuer, and it
is empty outside a rollover. There is still exactly one component in this estate that can mint a token, and
exactly one key with which it does so. The transient second piece of private material at that one component
is recorded as a deliberate divergence from the clause's literal count, made because an unrotatable signing
key is a worse security posture than a bounded overlap.

**The order to rotate in**

1. **Drain or accept.** Decide whether a bounded interval in which some in-flight tokens are refused is
   acceptable. Tokens are short-lived (five minutes), so waiting one token generation before and after is
   usually cheaper than any mitigation.
2. **Replace the contents of the file** `SECURITY_JWT_SIGNING_KEY_PATH` names, and change
   `Security:SigningKeyId` with it. The
   `kid` must change: a new key published under the old `kid` is the one shape a verifier cannot detect,
   because a cached entry would appear to match and the signature check would then fail with nothing
   pointing at the cause.
3. **Restart Security only.** No other service is reconfigured and none needs restarting — the whole
   point of the two intervals is that the verifiers converge on their own.
4. **Expect, and do not misread, the window.** Within `MetadataRefreshInterval` a newly minted token is
   accepted everywhere. Within the fallback lifetime — the same five minutes — a token signed by the
   previous key stops being accepted. A `401` carrying `IDX10503` during that window is the cache
   converging, not a misconfiguration.
5. **If the retired key is COMPROMISED, restart the verifiers.** The five-minute bound is short enough for
   an orderly rotation and is not short enough for an incident: a stolen key can mint fresh tokens, and
   those tokens are accepted until the fallback entry expires. Restarting Gateway, DataServices and
   Persistence drops every cached and recently-good configuration at once.
6. **Restarting a verifier is otherwise the escape hatch**, not the procedure. It converges that boundary
   at once and is worth doing only if the window is unacceptable for a particular deployment.

**If a rotation must be invisible**, lower `MetadataRefreshInterval` toward its one-second floor on the
verifiers *before* rotating and raise it afterwards. It is a floor on the fetch a REJECTED token can
provoke, so it is also the only rate limit on that fetch: a value near the floor makes a forged `kid` a
request amplifier aimed at the published key set, which is why the shipped default is seconds rather than
zero.

#### 4.2.2 `kid` selects a key, it does not gate one — measured

A token whose header names a `kid` **no verifier has ever published**, and a token carrying **no `kid` at
all**, are both **accepted** provided the signature verifies against a key the verifier holds. This was
measured on all three verifiers, and it is stock bearer-handler behaviour rather than a configuration
choice: `kid` is a *selection hint*, so when it does not match, the handler falls back to trying every
key it holds, and a signature made by the sole issuer's key verifies against that key whatever the header
claims. The reverse case is the one that matters and it fails correctly: a token signed by a key the
verifier does not hold is refused whatever `kid` it presents (§4.2.1 measures exactly that, twice).

**This is recorded rather than changed, and the reason is a security argument rather than a preference.**
Enforcing `kid` would make the header a second gate in front of the signature check — and a gate placed
in front of a cryptographic check is only ever as strong as the check behind it, while the failure it
adds is real: a rotation that changes the key identifier would begin refusing tokens whose signatures are
perfectly valid, turning an orderly rotation into an outage. The security property this topology rests on
is *one issuer, one minting key, verified by signature* (§4.1, §4.2), and a `kid` mismatch does not
weaken it because it cannot make an unsigned or wrongly-signed token verify. The hint earns its keep during
a rollover, which is the one time the published set holds two keys (§4.1.2): the header then tells a
verifier which to try first, and the fallback still resolves it if the hint is stale.

Two consequences worth stating so nobody depends on the wrong one. A `kid` **cannot** be used as an
authorization input anywhere in this system — audience and scope are the authorization inputs, and they
are validated. And on the day the issuing side gains a key ring, `kid` becomes a performance hint that
avoids trying every key; it still will not be a gate, because the signature remains the decision.

### 4.3 The issuance edge accepts two caller credentials, and mutual TLS is the per-pair fallback half

Mutual TLS is the documented fallback for a pair where a token issuer is inappropriate, it applies **to
that pair only** — adding a certificate path setting and a key path setting for those two services
rather than changing the system-wide model — and JSON Web Tokens remain the default on every other
edge. That is the general rule, and it has exactly one instance, which this register names rather than
leaving abstract:

> **`POST /v1/tokens` is the one operation a bearer token cannot protect**, because a caller cannot
> present a token in order to obtain its first one. `shared/PowerFramework.Contracts/OpenApi/
> security.v1.yaml` therefore declares **two** schemes and applies both to that operation as an override
> of the document-level bearer requirement, **either** of which satisfies it: `clientCredential`, an HTTP
> `Basic` credential naming a subject on Security's issuance roster, and `mutualTls`, a chain-verified
> client certificate. It defines `401` for a request presenting neither, or one this service does not
> hold, and `403` for an authenticated caller asking for a subject, audience or scope its roster entry
> does not permit.

**The settings are scaffolded rather than merely described, and that is a correction.** An earlier
revision of `orchestration/.env.example` listed them among its deliberate omissions on the reasoning that
nothing should be scaffolded before the pair adopts it. The reasoning was sound and its premise was
wrong: the pair has adopted it, since the published contract has required mutual TLS on issuance from the
moment it was authored. Without the settings the stack starts and then cannot issue a single credential,
so their absence was a functional defect rather than restraint. The seven variables now present are the
per-service server certificate and key (`<SERVICE>_TLS_CERTIFICATE_PATH` / `_KEY_PATH` — not
Security-specific, since all four services terminate TLS with the same default material), the trust
anchor Security validates presented client certificates against, and a client certificate and key for
each of the two services that request tokens — Gateway and DataServices. **Persistence has none**,
because it reads Security's anonymous key set and calls nothing else there, and provisioning a credential
for a caller that never authenticates would create material nothing consumes and nobody rotates.

> **Scaffolded is not the same as operable, and on this edge the difference is one mount.**
> The settings exist, both typed clients read them, the listener requests a client certificate, and
> **which issuers Security trusts IS established in application code**: `Security:MutualTls:ClientCaPath`
> is loaded at startup and installed as Kestrel's `ClientCertificateValidation` callback, which chains a
> presented certificate under `X509ChainTrustMode.CustomRootTrust` against that anchor alone. The OS trust
> store is deliberately not the decider, which is why `services/security-service/Dockerfile` installs no
> trust anchor and needs no root step. What no projection covers is this anchor:
> [`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) declares Compose secrets for
> the three **server-side** TLS files only and injects `SECURITY_MTLS_CLIENT_CA_PATH` verbatim as a
> container path with nothing projected behind it, so by default nothing places the file it names and with
> the path unset the callback defers to the platform's own verdict. **The certificate alternative on
> this edge is therefore declared and unexercised end to end** —
> one of the two accepted schemes, alongside the shared secret the documented bring-up supplies — and
> §4.1.1 says the same at its own point of use. The option not taken is recorded because it was a real
> choice: copying the CA into the runtime stage and running `update-ca-certificates` there would have kept
> validation in framework code, at the cost of a deployment-specific anchor in an image layer.

Three consequences belong in a secrets register specifically:

- **Every mutual-TLS setting is a PATH, and there is nowhere to put material.** The paths name files
  mounted read-only from the orchestration secret layer. There is no certificate body, no private key
  body and no passphrase key anywhere — not in the template, not in an `appsettings.json`, not in a
  container definition and not on a bound options type — so **no certificate and no private key is
  committed to this repository, and none is embedded in a container image.** The eight in-source sites in
  §2 are the standing illustration of what committing such material costs. The development generation
  recipe deliberately produces unencrypted key files protected by filesystem permissions rather than by a
  passphrase, because a passphrase would then need somewhere to live.
- **A half-configured pair is refused at startup.** Each caller's certificate and key are validated as a
  pair: a certificate cannot complete a handshake without its key and a key has nothing to present
  without its certificate, so both-or-neither is enforced with a named error rather than discovered at
  the first token request. No path is echoed into that error — a path is not itself a credential, but a
  startup log is the wrong place to publish where one is mounted.
- **Where the certificate scheme is in use, the issuance path must not sit behind a TLS-terminating
  proxy.** A client certificate authenticates the client to Security itself, so an intermediary that
  terminates TLS there either discards the certificate or leaves Security trusting a forwarded assertion
  of identity it cannot verify. Both outcomes defeat the sole-issuer topology this section exists to
  protect. Where the roster credential is in use the constraint does not arise, because the credential
  travels in a header the operation reads itself — which is a further reason the contract publishes both.
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.4 carries the deployment model, including what plaintext costs:
  the roster credential, every issued token and the key set are capturable on path, and the key set is
  substitutable. That exposure is **accepted** for the single-host, private-network bring-up the frozen
  environment describes, and for no other topology — which is exactly why this register treats the roster
  secret with the same handling rule as the signing key.

### 4.4 No secret is scaffolded for any deferred service

**No signing, verification or mutual-TLS variable is provisioned for DesignSystem, Documents,
Integration or ScriptBridge** (**C-D**).

The attached environment named five per-service signing secrets against a placeholder service roster.
The two that correspond to services which **do not exist in this phase** — the design-service and
localization-service names — are deliberately **not provisioned**. The reason is not tidiness:
**provisioning a credential for a service that does not exist creates an unowned secret** — one that
nothing consumes, nothing rotates, and nobody is accountable for, but which is nonetheless real and
must be protected. Unowned secrets are how credential sprawl starts.

This is consistent with the wider prohibition: the four deferred services receive no project, no
container definition, no test project, no partial implementation and no placeholder class, and the four
reserved Gateway routes are routing metadata rather than stubs ([`DEFERRED.md`](DEFERRED.md) §5.2).
Secret material is one more thing they do not receive.

### 4.5 The contract-level rule: raw key material never crosses the wire

> **Callers never send key bytes. A caller passes an opaque *key reference*, and Security resolves it
> against its own configured key store.**

This is the central design decision of the cryptographic service contract, and it discharges **C-F** at
the contract level rather than by review — there is no request field into which a key could be placed.
It is also a deliberate, documented **narrowing** of the legacy signature, and the legacy declarations
show exactly why the narrowing is necessary: every keyed operation takes the key as an ordinary
in-parameter — on the keyed hash family at `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26`, on
the symmetric families at `:L30-L61`, and on the RSA decrypt and sign families at `:L62-L73`. In
process, passing a key as an argument is unremarkable. On a wire, the same signature would place key
material into request bodies, into any log that records a request, and into every characterization
recording.

A consequence worth stating explicitly: **RSA key generation returns the public key and a reference to
the private key.** The generated private key is retained by Security and is never returned to the
caller. A caller that needs a signature calls the sign operation; it does not obtain the key with which
to produce one. [`CONTRACTS.md`](CONTRACTS.md) §5.2 defines the rule and its error behaviour in full.

### 4.6 The legacy anti-pattern this replaces, named explicitly

Site 1 is worth naming as the pattern being replaced rather than merely as a finding. It is a page that
**hardcodes an RSA private key in its own source and signs a JSON Web Signature with it**
(`tests/blink/test_jws.htm:L8-L22`, consumed at `:L23`). Everything wrong with it is structural rather
than accidental: the key is in the file, the file is in version control, the signing identity cannot be
rotated without editing source, and anyone who can read the page can mint signatures indistinguishable
from legitimate ones.

Security inverts every one of those properties. Its signing key arrives from configuration and exists
nowhere in source; it can be **rotated** without a code change, an edit to any tracked file or a rebuild,
and rotated with an overlap so the tokens already in flight keep verifying (§4.1.2); it is never returned
to a caller (§4.5);
and it is held by exactly one component (§4.2), so the set of things that can mint a token is
enumerable. That is the whole difference between the anti-pattern and the design, and it is why the
token topology is recorded in this document alongside the material it replaces.

---

## 5. Credential-bearing fields on the new boundaries

Sections 2 and 3 concern material that already exists in the repository. This section concerns
something different and forward-looking: **legacy fields that carry credential or credential-adjacent
material and that will now cross a network boundary or reach a log for the first time.**

The distinction matters because in-process these fields were unremarkable. A structure field passed
between two objects in one address space is not a disclosure. The same field serialized into a response
body, or written to a log, or captured into a characterization recording, is. **Decomposition is what
creates the exposure**, so handling each field is required *by* the transition rather than being a
security improvement layered on top of it. The cross-service contracts name each field and its rule and
then delegate the register to this document ([`CONTRACTS.md`](CONTRACTS.md) §5.2, §8.6 and §11.2).

| Field | Legacy locator | What it carries | Rule on a new boundary |
| --- | --- | --- | --- |
| `transactiondata.logpass` | `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L8` | The database account's password, as a member of the transaction descriptor | **Write-only.** Accepted inbound; **never echoed in a response, never logged, never captured into a recording** |
| `dberrordata.sqlsyntax` | `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L6` | The complete generated statement of a failing operation, **including interpolated literal values** | **Redacted** — one field, every literal replaced by a placeholder — before it crosses a boundary, reaches a log, or is written to a characterization recording. This row previously read "redacted, or structurally split into statement plus parameters", which contradicted §6.3's own record that the split was considered and rejected; the alternative is withdrawn here too. See §6.3 |
| The connection URI's optional credential parameter | `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L450-L456` | An optional password parameter in the storage connection URI grammar | **Write-only, and never logged.** Supplied by configuration injection; the assembled URI is never emitted in an error payload, a diagnostic, or a recording |
| Symmetric, keyed-hash and RSA private key parameters | `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26`, `:L30-L61`, `:L62-L73` | Raw key bytes, passed as ordinary in-parameters by the legacy | **Replaced by an opaque key reference.** Key bytes never cross the wire from a caller at all (§4.5) |

### 5.1 `logpass` is the clearest case, because the legacy round-trips it

The legacy in-process accessor moves this field in **both** directions: it is read in at
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L349` and written back out at `:L414`. In one
address space that symmetry is convenient and harmless. Across a boundary, the outbound half would place
the database password into a response body — and therefore into any log that records responses, and into
every characterization recording of one.

The field is therefore **narrowed to write-only**: inbound accepted, outbound never populated. It is
worth being precise about why this is not a behaviour change. **No legacy wire format exists** — the
legacy has no process boundary at all, so there is nothing whose observable protocol could be altered.
Narrowing here changes only what a **newly created** boundary is willing to emit, and that boundary had
no prior behaviour to preserve.

### 5.2 The same reasoning applies to the connection URI

The storage connection URI grammar accepts an optional password parameter alongside its mode, integrity-
check and journal-mode options. The parameter's **value** comes from configuration injection, never from
a literal. The **assembled URI** is treated as sensitive in its own right: it is never written to a log,
never returned in an error payload, and never captured into a recording, because a URI containing a
credential parameter is a credential.

This is the field [`CONTRACTS.md`](CONTRACTS.md) §11.2 refers to when it delegates the equivalent rule
for the connection URI's optional credential parameter to this register. The two connection-parameter
flags that travel in the same descriptor are documented at §11.4 there.

### 5.3 The generalisation

Stated once so that a field not yet enumerated is still governed: **any field whose value is a
credential, a key, or a statement containing interpolated literals is write-only inbound and redacted
outbound, on every boundary this refactor creates.** A field added later inherits the rule by virtue of
its content, not by being listed here. Where a rule cannot be applied without losing behaviour the
system depends on, the contract is **narrowed with a defined error** rather than widened with a guess —
and the narrowing is recorded, as the two above are.

### 5.4 Data protection key material is deliberately ephemeral, and nothing is written to disk

This one is not a wire field. It is key material the **platform** creates whether or not anything asks
for it, and it is recorded here because the register's subject is secrets at rest, not only secrets in
transit.

**What the default does.** `AddAuthentication` registers the ASP.NET Core data-protection stack — the
authentication assembly calls `AddDataProtection` for the ticket formats its remote handlers use, and
none of these four services registers a remote handler. Data protection's own eager initialiser then
materialises a key ring during host start. That was **measured on all four services** rather than
inferred: each wrote an unencrypted private key into the process's user profile at startup, observed at
`/root/.aspnet/DataProtection-Keys`, and one host that afterwards *failed to bind its port* had already
written it. Two of the four log the fact and two do not, purely because of their log filters, so the
record is easy to miss: Gateway and DataServices raise `Microsoft.AspNetCore` to Information in
Development, Persistence leaves it at Warning, and Security pins `Microsoft.AspNetCore.DataProtection` to
Warning specifically.

**Why that is the wrong resting state.** Nothing in any of the four services protects a payload. Inbound
authentication is bearer-token validation against the published key set (§4.2) — stateless, no protector
— and there is no cookie, no session, no antiforgery token and no protected payload that outlives a
request. The default therefore writes key material to disk **for no consumer**: a secret at rest with no
purpose, created per container and shared with nothing, which is the one shape this register has no
tolerance for.

**The posture, and its failure mode.** All four composition roots configure
`KeyManagementOptions.XmlRepository` with an in-memory `IXmlRepository` — `InMemoryDataProtectionKeyRepository`,
declared once per service beside its own composition root. Keys live in the process and die with it,
nothing reaches the filesystem, and — this is the load-bearing half — a future capability that genuinely
needs a durable protector fails **immediately and visibly on the first restart**, instead of working on
one replica and failing on the next. Of the two failures on offer, the intermittent cross-replica one is
strictly worse, because it presents as flakiness rather than as a missing decision.

🔴 **The obvious fix is the one that does not work, and it was measured rather than reasoned about.**
Replacing `IDataProtectionProvider` with the framework's ephemeral provider — the documented way to ask
for non-persistent data protection — was tried first, and a host wired that way **still wrote a key file
on every start**. The reason is that the eager initialiser warms the **key-management stack**, not
whichever provider happens to be registered, so swapping the provider changes who *reads* the ring and
not who *creates* it. Redirecting the repository removes the file-system repository from the graph
entirely, which is why it is the layer the four services configure and the layer their tests assert on. A
suite that asserted the provider type would have gone green over a key file being written beside it.

**One log record survives, and it is deliberately left alone.** Every service still writes
`XmlKeyManager[35]` at Warning — *"No XML encryptor configured. Key {id} may be persisted to storage in
unencrypted form."* Its premise is false here: with an in-memory repository nothing is persisted at all,
and the key-creation record `XmlKeyManager[58]` names a key that never leaves the process. Configuring a
do-nothing encryptor would silence it, and that is exactly why it is not done — **the warning is the alarm
that must fire the day the repository becomes persistent**, which is the change the last paragraph of this
section anticipates. Disarming it now would remove the one automatic signal that the decision recorded
here had expired. A reader meeting it should treat it as expected, and as load-bearing.

**Why not persist it properly instead.** Persisting the ring would relocate the hazard rather than remove
it. At-rest encryption of a persisted ring needs an X.509 certificate this deployment does not provision;
DPAPI is Windows-only and the target is Linux containers; and a shared ring needs shared storage that no
part of the orchestration provides. Each of those is a new required secret or a new required mount
introduced for a facility with no consumer — new failure modes bought with no benefit.

**What a later phase must do.** The moment antiforgery, cookies, or any protected payload that must
survive a restart or be readable by a second replica is introduced, this decision expires. The
replacement is a **shared** key ring in storage both replicas can read, **encrypted at rest** with a
certificate the orchestration provisions, configured in one place per service beside the call this
paragraph documents. The symptom that announces the need is unmistakable and is why the loud failure was
chosen: protected payloads stop round-tripping across a restart or between replicas.

**Constraint position.** This is not a behaviour change under C-B. There is no legacy analogue to
preserve or to break — the key ring is an artifact of the ASP.NET Core hosting choice this refactor
introduced, and the legacy framework, a library with no process of its own, has nothing that corresponds
to it.

---

## 6. Log hygiene — the two controls this refactor adds

Everywhere else, this refactor **preserves** legacy behaviour, including legacy defects. These are the
only two places it **adds** a control, so the justification for each is set out rather than assumed.
Both concern what reaches an operator's log: §6.1–§6.4 cover the statement text a database error carries,
and §6.5 covers the caller-chosen identifiers a diagnostic names.

### 6.1 What the legacy does

Two facts, each verified at its locator:

- **The statement field carries the complete generated statement, literals included.**
  `dberrordata.srs:L6` declares `sqlsyntax` as part of the database-error structure, and the command
  task passes the failing statement text **straight into the error event** in that position at
  `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L110`. Nothing filters it on the
  way.
- **The legacy logger performs no redaction at all.** There is no filtering stage, no allow-list, and no
  concept of a sensitive field. Every literal in a failing statement therefore reaches whatever consumes
  the error.

### 6.2 The mechanical root: the runtime may not be using bind variables

This is the part that turns "statements might contain literals" into "statements systematically contain
literals". The connection parameter string is parsed for a bind-disabling flag at
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129`, alongside a
national-character-binding flag.

**When bind is disabled, the runtime does not use bind variables — it interpolates values directly into
the statement text.** So on such a connection, every parameter value a failing statement touched is
present in `sqlsyntax` as a literal. Personal data, identifiers, credentials embedded in data columns:
whatever was in the row is in the error field.

In process, the audience for that field was the calling application. Once Persistence is a separate
service, **the audience includes a network peer and a log aggregator**, neither of which existed before.

### 6.3 The control, and why it is not a behavioural change

The error payload's statement field is **redacted** before it crosses a boundary or reaches a log. **The
wire carries one redacted string field and nothing beside it.**

**The shape is decided rather than open, and this is the record of the decision.** Two shapes were
permitted when the control was specified — a single redacted field, or a statement plus a separate
parameter collection — and the **single redacted field** is the one implemented and published:
`shared/PowerFramework.Contracts/Proto/common.v1.proto` declares `string sqlsyntax = 3` with the
redaction rule stated on the field, `DbError` has **no** `parameters` member, and
`services/persistence-service/PowerFramework.Persistence/Errors/SqlRedactor.cs` is the one component that
produces the value. Adding a parameter collection now would be a contract revision, not a refinement.
The alternative is CLOSED rather than open, here and in [`CONTRACTS.md`](CONTRACTS.md) §8.6. The reason the
single field wins belongs in a secrets register: **a parameter collection is itself the sensitive data.**
Separating a literal from its statement moves the value, it does not protect it, so splitting produces two
fields to redact instead of one and a second place for a future change to forget.

This is a **logging and transport control, not a behavioural change**, and the distinction rests on
three points:

1. **The observable generated statement is preserved byte for byte wherever behaviour depends on it.**
   Parity for the paging rewriters and clause construction is byte-exact generated SQL, sentinel
   identifiers and count aliases included ([`CONTRACTS.md`](CONTRACTS.md) §8.4,
   [`docs/PARITY.md`](PARITY.md)). Redaction applies to the **error and log projection** of a statement, not
   to the statement the engine executes or to any statement a parity test compares.
2. **The boundary being narrowed is new.** As in §5.1, there is no prior wire format whose contract
   could be broken. The field's in-process behaviour is untouched.
3. **The .NET implementation uses parameterized commands internally while preserving observable
   behaviour.** The implementation may be safer than the legacy exactly where the difference is
   unobservable; where the generated statement is observable, it matches.

Point 3 is the one that reconciles this section with **C-B**. Nothing observable is improved. What
changes is that a field which was always sensitive stops being broadcast to consumers that did not
previously exist.

### 6.4 Two related legacy defects, documented rather than silently changed

Both are injection exposures in the same family. Both are **preserved as behaviour and documented as
defects** — they are not quietly fixed, because the generated statement and the produced filter are
observable and parity depends on them.

| Defect | Locator | What it does |
| --- | --- | --- |
| The where-clause setter accepts a **raw clause string** | `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L269-L284`, applied at `:L691` | The clause arrives as an unvalidated string and is stored verbatim; the only validation rejects a non-positive index or an empty clause. It is later spliced into the statement through the parser's clause-modification path |
| The drop-down search service **interpolates user data unescaped** | `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323`, and likewise at `:L319` | Search text is concatenated directly into a filter expression, inside quoting it does not escape |

The posture on both is the same as everywhere else in this refactor: **reproduce the observable
behaviour, document the defect at its point of reproduction, and do not silently correct it.** Each is
annotated where it is reproduced so that a future reader cannot mistake a deliberately preserved defect
for an implementation error. [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.5 explains the exposure
mechanically; [`CONTRACTS.md`](CONTRACTS.md) §8.3 names the clause-modification site at the contract
level.

### 6.5 Caller-chosen identifiers are neutralized before they reach a log record

The second added control, and it is the same shape of decision as the first: a value that is entirely the
caller's choice is written into a **rendered** operator record, and rendering is where it becomes
dangerous.

**What the exposure is.** Two of the values DataServices records for an operator arrive on the request and
are chosen by whoever sends it: the **DataWindow handle** a request names, and the **column name** a
rejected value was addressed by — a `NoSuchColumn` refusal carries the caller's own name verbatim so that
an operator can see which column was meant. The shipped console and file logging providers render one
record per line, so a caller that places a carriage return and a line feed inside one of those values
**splits one record into two** and dictates the text of the second. The forged line can imitate this
service's own diagnostics, so neither an operator reading the file nor a log-shipping pipeline parsing it
can tell it from a real record. This is log forging, CWE-117, and it is the logging analogue of the
statement exposure above: the value is legitimate to *carry*, and unsafe to *render*.

**The control.**
`shared/PowerFramework.Shared.Diagnostics/FaultDiagnostics.cs` renders one value safe for a record -
`LogSafeText.Render`, shared by all three services that log so the rule is stated once rather than per
service. Its rule is stated over Unicode **categories** rather than over a hand-picked list of characters,
because reasoning about each one separately is how one gets missed:

| Category | Why it is encoded |
| --- | --- |
| `Control` | Carriage return and line feed **end a record** in every line-oriented sink, which is the forging vector itself. The rest of the category travels with them, including `U+0085 NEXT LINE`, which several readers treat as a break |
| `LineSeparator`, `ParagraphSeparator` | `U+2028` and `U+2029` are **not** control characters, so a check written only against an `IsControl` test admits them — yet they are line breaks to JSON-aware and JavaScript-based log viewers |
| `Format` | The bidirectional overrides live here. They reorder the **visible** text of a line without changing its bytes, so a record can display an identifier other than the one it stores — the same class of deception as a forged second line |

Three properties of the control are deliberate:

- **Encoded, not stripped.** A stripped carriage return leaves a line that reads as an ordinary name,
  which destroys exactly the evidence an operator investigating a forged record needs. The escape records
  that a control character was sent, which one, and where in the value it sat. A backslash is doubled with
  them, so a caller writing the escape sequence out in full cannot imitate one.
- **Bounded in length.** Neither identifier has a length limit on the wire, so a record is capped and
  states how many of the caller's own characters were discarded.
- **The log only. The wire contract is untouched.** `RowValidationError.column_name` still carries the
  caller's bytes exactly as they arrived, because the caller needs to recognise what it sent and a
  structured protobuf field involves no rendering. Neutralizing the response as well would have changed a
  published contract to solve a logging problem, which **C-B** forbids.

**Why this is not a behavioural change either**, by the same argument §6.3 makes for redaction: the
legacy delivered these diagnostics through `MessageBox`/`MessageBoxEx` dialogs, which have no log record
to forge and no line-oriented sink behind them. The hazard is created by the decomposition — a service
with a log file — so a control against it is required *by* the transition rather than layered on top of
preserved behaviour. Nothing observable to a caller changes.

**The standing rule, so it does not have to be rediscovered:** every string argument passed to a log
record in Gateway, DataServices and Persistence goes through the neutralizer — handle, column name, session
identifier, correlation identifier, principal, DBMS and server names — applied uniformly rather than only
where a value is provably caller-chosen. A rule with exceptions has to be re-argued for every new record;
for a server-minted value the call is a no-op, so uniformity costs nothing. Numeric arguments are
deliberately left unwrapped and that is recorded rather than overlooked: a `long` cannot forge a record.

**The exception-valued arguments are covered by the same file and a separate rule.** A raw exception
rendered into a record carries a provider's own message, and a provider's message can carry the statement
text §6.3 redacts — so `ExceptionChain.DescribeTypes` renders the *type* chain, outermost-first, bounded in
depth and terminating on a cycle, and Persistence's `Errors/FaultRecord.cs` composes that chain with the
redacted message. An exception OBJECT survives only where the framework's own sink will not render its
message into a shipped record — `Debug` and `Trace`, which exist to inform a developer at a console — and
in the documented terminal arms **C-B** requires be preserved verbatim from the legacy `systemerror`
dialog.

---

## 7. Cryptographic weak defaults are preserved as annotated defaults

The legacy cryptographic defaults are weak by modern standards. **They are kept exactly as they are.**

The framing matters, because this section is the one most likely to be misread as a backlog of things to
fix. It is not. Correcting a legacy default would violate the behaviour-preservation mandate (**C-B**),
which forbids improvements as firmly as it forbids regressions. So each default is **preserved as the
default and annotated in the contract description as a known legacy weakness**, letting a caller see the
risk **without the behaviour changing**.

> **Annotation is the remediation here.** No default below is silently strengthened, and no "safe mode"
> is offered alongside it. Strengthening any of them would be a behavioural change the mandate forbids —
> so none of the entries below is a defect to be closed, and none should be read as one.

| # | Preserved legacy behaviour | Evidence |
| --- | --- | --- |
| 1 | **The default symmetric mode is ECB.** Overloads that omit the mode selector run in ECB, so identical plaintext blocks produce identical ciphertext blocks and structure is preserved | The default mode constant is declared equal to the ECB constant at `ws_objects/pfw.shared.pbl.src/enums.sru:L946`; the mode-omitting overloads are exactly `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30`, `:L32`, `:L34` and `:L36`, with `:L31`, `:L33`, `:L35` and `:L37` carrying the mode |
| 2 | **The default RSA padding is PKCS#1 v1.5** | The default padding constant is declared equal to the PKCS#1 constant at `enums.sru:L949-L951`, so an overload that omits the selector signs and encrypts under PKCS#1 |
| 3 | **No-padding is explicitly unsupported** — the legacy rejects it, and so does the port | Established by **absence**: exactly two padding values exist, PKCS#1 and OAEP, at `enums.sru:L949-L950`. **There is no "none" value to select**, so the rejection is the legacy's own rather than an addition |
| 4 | **PKCS#5-family symmetric padding only, and not selectable** | Established by **absence**: there is no symmetric padding constant of any kind anywhere in `enums.sru`, the cipher block at `:L936-L946` exposes type and mode and nothing else, and none of the 32 symmetric overloads at `n_crypto.sru:L30-L61` carries a padding parameter. The contract carries no field for padding, because offering one would imply a choice the legacy never had |
| 5 | **No key-derivation function is reachable at all**, and there is no salt concept — so a passphrase is used as **raw key bytes** | Established by **absence**: no PBKDF2, scrypt, bcrypt, Argon2 or salt constant exists in `enums.sru`, and no signature in `n_crypto.sru:L30-L61` accepts an iteration count or a salt. Whatever the caller supplies as key material *is* the key |
| 6 | **No authenticated encryption** — no GCM, CCM or Poly1305 — so ciphertext carries **no integrity tag** | Established by **absence**: the mode set is exactly ECB, CBC and CFB at `enums.sru:L943-L945`. A caller needing ciphertext integrity must obtain it separately, for instance through the keyed-hash operations |
| 7 | **1024-bit RSA remains a legal key size**, and the legacy demonstration uses it | The 1024-bit constant is a declared, first-class value alongside 2048 and 4096 at `enums.sru:L965-L967`. It is annotated as a legacy-compatibility value and is **not** removed from the accepted set |
| 8 | **The same hash set is declared for the RSA signature hash**, so MD5 is a legal **signature**-hash selector and remains one | The declaring comment at `enums.sru:L927` names the set's consumers as `Hash`, **`RSASign` and `VerifyRSASign`**: one set, three consumers. All four signature overloads accept it as a hash-type argument [`n_crypto.sru:L70-L73`]. The weakness is the *scope* of an otherwise ordinary constant set, and it is annotated rather than corrected. CRC32 is the sole exception, and not on strength grounds: there is no HMAC-over-checksum or RSA-over-checksum construction to implement at all, so the keyed and signing operations publish a narrowed `CryptoKeyedHashType` while the unkeyed digests keep the full set — see `docs/CONTRACTS.md`, narrowing N1 |

Items **3, 4, 5 and 6** rest on absence, and §1.3 requires that to be said rather than glossed. Here the
absence is exactly the right kind of evidence: **a capability the legacy cannot express is a capability
the port must not offer**, because offering it would be a new feature (**C-B**). There is no constant to
select and no signature that accepts one.

**Separately from the eight — and not itself a weakness — the algorithm identifier sets are preserved
exactly**, values and spellings alike. A rename would silently invalidate every stored characterization
comparison, because these exact spellings appear in serialized payloads, log records and recordings.
That is a preservation *rule* rather than a defect, which is why it is not counted as a ninth item.

The identifier sets that rule covers, verified value by value:

| Set | Identifiers and values | Locator |
| --- | --- | --- |
| Hash type | `CRYPTO_HASH_MD5` = 0, `CRYPTO_HASH_SHA1` = 1, `CRYPTO_HASH_SHA256` = 2, `CRYPTO_HASH_SHA384` = 3, `CRYPTO_HASH_SHA512` = 4, `CRYPTO_HASH_CRC32` = 5 | `enums.sru:L928-L933` |
| Symmetric cipher | `CRYPTO_SYMCRYPT_TYPE_DES` = 0, `..._3DES` = 1, `..._AES128` = 2, `..._AES192` = 3, `..._AES256` = 4 | `enums.sru:L936-L940` |
| Symmetric mode | `CRYPTO_SYMCRYPT_MODE_ECB` = 0, `..._CBC` = 1, `..._CFB` = 2, `..._DEFAULT` = ECB | `enums.sru:L943-L946` |
| RSA padding | `CRYPTO_RSA_PADDING_PKCS1` = 0, `CRYPTO_RSA_PADDING_OAEP` = 1, `..._DEFAULT` = PKCS#1 | `enums.sru:L949-L951` |
| RSA key size | `CRYPTO_RSA_BITS_1024` = 1024, `..._2048` = 2048, `..._4096` = 4096 | `enums.sru:L965-L967` |
| Encoding | `CRYPTO_ENCODING_BASE64` = 0, `CRYPTO_ENCODING_HEX` = 1 | `enums.sru:L924-L925` |

These are **algorithm identifiers, not secrets** — they name which algorithm to use and disclose
nothing. They are enumerated here because a reader auditing the cryptographic surface needs to confirm
that the preserved sets are the legacy sets and nothing has been quietly added or dropped.

Two related points belong with this section:

- **The random generators are determinism seams, not weaknesses.** The random-blob, random-string and
  GUID generators at `n_crypto.sru:L14-L18` are the primary non-determinism sources in the in-scope
  estate. Characterization compares a recorded legacy run against a target run, so a value that differs
  on every execution must be masked on **both** sides. The provider behind them is therefore injected so
  a test can substitute a deterministic double while production uses the platform generator. The full
  seam register is in [`docs/PARITY.md`](PARITY.md); the contract-side note is
  [`CONTRACTS.md`](CONTRACTS.md) §5.4.
- **Weak defaults and the key-reference rule are independent.** Preserving ECB does not mean preserving
  the legacy's habit of passing key bytes as arguments. The *algorithm* behaviour is preserved (§7); the
  *transport* of key material is narrowed (§4.5). One is behaviour, the other is a property of a
  boundary that did not previously exist.

[`CONTRACTS.md`](CONTRACTS.md) §5.3 carries the same eight items in the same order, with the exact
annotation text each contract description emits, and
[`OpenApi/security.v1.yaml`](../shared/PowerFramework.Contracts/OpenApi/security.v1.yaml) §6 carries
them a third time at the boundary itself. All three lists are numbered identically so a reader can
check them against one another item by item.

---

## 8. Constraint compliance and cross-references

### 8.1 How this document honours the constraints that govern it

No user-specified rules exist (§1.2), so the governing constraints are the brief's own clauses and the
attached environment's setup instructions. Six of them bear on this document directly.

| Constraint | What it requires of this document | How this document satisfies it |
| --- | --- | --- |
| **C-F** — nothing hardcoded may be carried forward; the three named sites are a floor, not a ceiling | Record every site with locator, severity and required action; reproduce no value; distinguish the named from the newly found | §2.3 records all **eight** in-source sites and §2.7 all **three** binary sites. §2.1 states the arithmetic — three named, eight plus three found — and the "Named in requirements?" column marks the **five** newly found. No value appears anywhere (§1.1, §8.2) |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | Determine the remediation posture accordingly, and explain why deletion is wrong | §3.1 shows all eight in-source sites lie in the read-only region. §3.2 states the posture. §3.3 explains why deletion would corrupt the oracle and buy nothing, and confirms this document **never** recommends editing any legacy path or any of the five pre-existing Chinese documents. This register is purely additive |
| **C-G** — no new attack surface: every newly created boundary is authenticated | Carry the token topology | §4 in full: one signing secret (§4.1), what is and is not enforced about it (§4.1.1), sole issuer and three verifiers (§4.2), mutual TLS as a per-pair fallback with its trust anchor recorded as **declared but not yet installed** (§4.3), the opaque key-reference rule (§4.5) |
| **C-D** — do not implement the four deferred services | Scaffold no secret material for any of them | §4.4. No signing, verification or mutual-TLS variable is provisioned for DesignSystem, Documents, Integration or ScriptBridge, and the environment's design-service and localization-service names are explicitly not provisioned |
| **C-B** — no new features, no behaviour improvements, no performance objective | Preserve the weak defaults and annotate them; assert no performance figure | §7 preserves all eight and states that annotation **is** the remediation. §6.3 justifies the one added control as a logging and transport control rather than a behavioural change. No performance, latency, throughput, availability or service-level figure appears anywhere in this document, because the repository publishes none |
| **C-K** — document every technology-specific and boundary-specific decision | Document the remediation decisions, the operational follow-ups, and the cleared false positives | §3 carries the posture and its reasoning. §3.6 surfaces both operational follow-ups. §2.10 clears the false positives so remediation is not misdirected, and §2.9 records a corrected attribution for the same reason |

### 8.2 The value checks, and their results

§1.1 promised that this document's own compliance was verified rather than asserted. Three checks were
run against the finished text.

| Check | What it looked for | Result |
| --- | --- | --- |
| **1 — Marker and keyword search** | PEM block markers for private keys, public keys and certificates; and the credential keywords a secret-scanner looks for | **Pass.** Every occurrence is either the **name** of a configuration variable or a **description of a material class**. No occurrence is a value |
| **2 — Long base64-run scan** | Any unbroken run of base64-alphabet characters long enough to carry encoded key or certificate material | **Pass.** No such run exists anywhere in this document. This is the decisive check, because the MQTT material carries **no** block marker and would pass check 1 while being fully present (§1.1) |
| **3 — Near-miss review** | Truncated, elided, partially masked or "illustrative" values resembling real material; and any live endpoint address | **Pass.** None present. No prefix, no fragment, no example key, and no host address |

Every locator cited in this document was also verified to resolve — the file exists, and the cited line
number falls within that file's length — using file metadata and line counts only, without printing the
content of any cited line (§1.3).

### 8.3 What this document does not claim

Stated so that the register's limits are as legible as its findings:

- **It does not claim the inventory is exhaustive of all time.** It is the result of the three search
  families in §2.2 at the state of the repository recorded here. A new search family could find more —
  which is precisely the lesson of §2.1, where the three named sites turned out to be a floor.
- **It does not claim that deleting anything from the legacy tree would help.** It claims the opposite,
  with reasons (§3.3).
- **It does not claim the two operational follow-ups are complete.** They are open, they are
  operational, and no code change closes them (§3.6).
- **It does not claim the binary sites have been assessed as safe.** They are classified
  **Informational** because they are not source-remediable, and because their contents cannot be
  verified from here (§2.7). "Not remediable by this refactor" is not "cleared".
- **It does not claim any container bring-up has been verified.** No orchestration bring-up or health
  gate is asserted as exercised anywhere in this document. The build-context exclusions in §3.5 are
  verified by reading `.dockerignore` at the cited locators — that is a static verification of the
  exclusion list, and nothing more is claimed from it.
- **It does not claim mutual-TLS caller authentication on `POST /v1/tokens` is operable.** The settings
  and the typed clients exist and the listener requests a certificate, but no container installs the
  trust anchor and no Compose manifest mounts it, so no presented client certificate has ever been
  validated. §4.1.1 and §4.3 both record that as a pending implementation with two named options.
- **What it claims about the signing key is exactly what the code enforces, and both halves of this bullet
  have been wrong in opposite directions before.** §4.1.1 states the enforcement: a fixed import sequence
  whose failure refuses startup, a **bound and validated** format setting, and a **2048-bit floor enforced
  twice** — in the options validator and again in the signing-key provider — held in a constant with no
  setting to lower it. An intermediate revision withdrew that floor, reading AAP §0.6.6.4's 1024-bit
  allowance as governing the issuer; the allowance governs the C-02 crypto surface, and §4.1.1 records why
  the distinction is the requirement rather than a convenience. **Rotation now exists** and is an overlapped
  rollover (§4.1.2): an active key, an optional retiring key published beside it, distinct `kid`s, and a
  documented overlap that must be at least the token lifetime plus clock skew plus the verifiers' key-set
  staleness. What is still NOT claimed is automation — every step of §4.1.2 is manual, nothing schedules a
  rotation, and no code enforces the wait in step 5.
- **It does not claim any user-specified rule governs this work.** None exists (§1.2); the bar applied
  in their place is stated there rather than assumed.
- **It reproduces no secret value of any kind** — the claim this document opens with, and the one every
  other claim in it depends on.

### 8.4 Cross-references

This register is the upstream for secret locators, severities, required actions and the token topology.
It deliberately does not duplicate its siblings:

| For | See |
| --- | --- |
| Service boundaries, the port and transport map, storage, the capability gate, the sole-issuer topology in architectural terms, and the SQL-injection exposure explained mechanically | [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.5, §9, §10.2 |
| The token service and its sole-issuer property (C-01), the cryptographic service with its key-reference rule and the eight annotated defaults (C-02), the redaction rule at the contract level (C-05, C-08), and the reserved deferred routes | [`CONTRACTS.md`](CONTRACTS.md) §5.2, §5.3, §8.3, §8.6, §11.2, §11.4 |
| The characterization model, the fixture corpus drawn from the test and demonstration libraries, the determinism seams, and the byte-exact parity criteria referenced in §6.3 | [`docs/PARITY.md`](PARITY.md) |
| Why the secret-bearing libraries are deferred or permanently out of scope, and the same corrected MQTT attribution from the deferral side | [`DEFERRED.md`](DEFERRED.md) §3, §4.5, §5.2 |
| The full-estate library-to-destination assignment, and the same corrected attribution from the mapping side | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §9, §12.2 |
| The variable roster an operator must populate, with empty values by design | `orchestration/.env.example` |
| Build and test commands, and per-service build independence | [`BUILD.md`](BUILD.md) |

---

**Summary in one paragraph.** Three secrets were named; eleven sites were found, and the two most
severe — a certificate paired with its matching private key, and a live administrative credential
triple in a comment block — were both among the five nobody had named. Every in-source site sits inside
the read-only behavioural oracle, and the material is already in version-control history, so deleting
the literals would damage the oracle and fix nothing. The remedy is therefore to **never replicate,
document, and rotate**: this register documents, configuration injection guarantees no replication, and
two rotations remain open as operational actions that no code change can close.
