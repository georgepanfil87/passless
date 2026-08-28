# Passless

Most authentication servers that support passkeys support them incompletely. The
WebAuthn signature gets verified, and then the parts that actually carry the
security guarantee get skipped: the challenge is never marked consumed, so it can
be replayed; the origin and RP ID in the client data are parsed but not compared;
the signature counter is read and discarded; and the refresh token handed out
afterwards is a long-lived bearer string with no rotation and no way to tell a
stolen copy from the real one. Passless is a complete, readable implementation of
exactly those parts — written for backend engineers who have been handed "add
passkey login" and want to see the ceremonies, the token rotation and the
per-device revocation model done fully rather than sketched.

It is a reference implementation and a portfolio project, not a product. It is
not a hosted identity provider and does not want to be your IdP.

## What this implements

Four things, each of which is the reason the repository exists:

1. **Complete WebAuthn ceremonies** — registration and assertion, with
   single-use challenge enforcement, origin and RP ID validation, and signature
   counter handling that accounts for the authenticators which legitimately
   always report zero.
2. **Refresh token rotation with family reuse detection** — every refresh mints
   a new token and consumes the old one. Presenting a consumed token is treated
   as evidence of theft and invalidates the entire family.
3. **Per-device sessions** — each authenticator gets its own session, listable
   and individually revocable, so losing one device does not mean signing out
   everywhere.
4. **Biometric flows tested in CI** — the ceremonies run end to end against a
   CDP virtual authenticator, so both the happy path and the rejection paths are
   covered without a human touching a fingerprint sensor.

> **Status: in progress.** (1) and (2) are complete — both WebAuthn ceremonies
> with single-use challenges, explicit origin and RP ID validation and signature
> counter handling; and refresh token rotation with family-wide reuse detection.
> Per-device session listing and revocation (3) has its data model but no
> endpoints yet, and (4) is not built. See
> [docs/assertion-ceremony.md](docs/assertion-ceremony.md) for the assertion
> sequence diagram.

## Security properties

These are the invariants the implementation is required to hold. Each will gain a
link to the code that enforces it and the test that proves the failure case is
rejected.

- Refresh tokens are hashed at rest and never logged in cleartext.
- Challenges are single-use, short-lived, and bound to the ceremony that issued
  them.
- Every security-relevant event writes an immutable audit record, enforced at the
  database rather than by convention.
- Reuse of a consumed refresh token invalidates the whole token family.
- All secret comparisons are timing-safe.
- No secret, token or credential public key appears in a log line.

Cryptographic primitives are **not** hand-rolled. CBOR decoding, COSE key parsing
and attestation chain verification come from
[Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib); Argon2id comes
from Konscious.Security.Cryptography. What is implemented here is the protocol
and state handling around them, which is where the bugs actually are.

## Architecture

See [docs/DESIGN.md](docs/DESIGN.md) for the design and
[docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) for what this defends against and
what it explicitly does not. Decisions with consequences are recorded as ADRs in
[docs/adr/](docs/adr/).

## Running it locally

### Prerequisites

- .NET SDK 8.0.4xx
- Node 20 or newer
- Docker with Compose v2

### 1. Start the backing services

```bash
cp .env.example .env && docker compose up -d
```

Postgres 16 on `5432`, Redis on `6379`.

### 2. Generate and trust the local certificate

```bash
./scripts/dev-certs.sh
```

The script writes a certificate and a private key into `certs/`, which is
git-ignored, and trusts the certificate on your machine. Kestrel and the Angular
dev server both read that PEM pair directly. It takes one of two paths:

**If [mkcert](https://github.com/FiloSottile/mkcert) is installed**, it issues a
certificate from mkcert's own local CA covering `localhost`, `passless.localhost`
and the loopback addresses. `mkcert -install` puts that CA into the system and
browser trust stores the first time it runs, so nothing further is needed. This
is the better path if you want to work under a hostname other than `localhost`.

**Otherwise** it falls back to `dotnet dev-certs`, which ships with the SDK:

```bash
dotnet dev-certs https --trust
```

On macOS this adds the certificate to the login keychain and prompts for your
password. On Windows it goes into the current user's trusted root store. On Linux
`--trust` is best-effort — you may need to copy the exported PEM into
`/usr/local/share/ca-certificates` and run `update-ca-certificates` yourself.
Firefox keeps a trust store separate from the operating system on every platform,
so it needs the certificate imported by hand under **Settings → Privacy &
Security → Certificates → View Certificates → Authorities**.

The SDK's development certificate covers `localhost` and nothing else. Install
mkcert and re-run the script if you need more.

To undo the trust decision later: `dotnet dev-certs https --clean`, or
`mkcert -uninstall` to remove the mkcert CA.

### 3. Run the API and the client

```bash
dotnet run --project src/Passless.Api
```

```bash
npm --prefix client start
```

The client serves on `https://localhost:4200` and proxies `/api` to the API on
`https://localhost:5001`. The proxy is not a convenience: it puts the SPA and the
API on a single browser origin, so cookies are first-party and the RP ID is
unambiguously `localhost`. Running the client on one origin and the API on
another would mean developing against cross-site cookie behaviour that the
production topology will not have.

### Why the certificate is necessary: WebAuthn and secure contexts

WebAuthn is exposed only to **secure contexts**. `navigator.credentials.create()`
and `.get()` are simply absent otherwise, so a page served over plain HTTP from
anything but loopback cannot begin a ceremony at all.

The W3C Secure Contexts specification treats a small set of origins as
"potentially trustworthy" without TLS: `127.0.0.1/8`, `::1`, and `localhost`
together with any `*.localhost` subdomain. That is why passkey demos work over
`http://localhost` — the browser knows the traffic never leaves the machine, so
there is no network position for an attacker to occupy.

We serve HTTPS in development anyway, deliberately. Running over TLS locally means
the `Secure` cookie attribute is exercised exactly as it will be in production,
the RP ID is derived from a real HTTPS origin, and mixed-content and cross-origin
problems surface on a laptop instead of in staging. Developing over
`http://localhost` and deploying over HTTPS means the first real test of the
production cookie flags happens in production.

One further constraint, worth knowing before changing any hostname: the RP ID
must equal the origin's effective domain or be a registrable domain suffix of it.
An origin of `https://localhost` may use an RP ID of `localhost` and nothing else.
Credentials are scoped to the RP ID, so changing it later invalidates every
passkey already registered — it is not a value to pick casually.

## Tests

```bash
dotnet test tests/Passless.IntegrationTests
```

Integration tests run against real Postgres and Redis through Testcontainers — no
in-memory provider, no mocked cache. The behaviour under test is largely unique
constraints, transaction isolation and atomic counter operations, which are
precisely the things a fake gets right by pretending. Docker must be running;
containers are created and destroyed per run.

```bash
dotnet test tests/Passless.E2ETests
```

End-to-end tests drive Chromium through Playwright with a CDP virtual
authenticator standing in for a security key. Chromium is not a preference — the
WebAuthn virtual authenticator is a Chrome DevTools Protocol domain and has no
equivalent in Firefox or WebKit.

```bash
npm --prefix client test
```

The client also serves a design preview at `/preview`, which renders every base
component in both themes from a single component tree. The visual rationale and
the full token reference are in [docs/DESIGN.md](docs/DESIGN.md).

Every ceremony will have at least one test asserting that the failure case is
*rejected*. A suite that only proves the happy path works tells you nothing about
whether the security control exists at all.

## Repository layout

| Path                              | What it is                                     |
| --------------------------------- | ---------------------------------------------- |
| `src/Passless.Core`               | Domain model. No framework dependencies.       |
| `src/Passless.Infrastructure`     | EF Core context, configurations, migrations.   |
| `src/Passless.Api`                | Minimal API host and endpoints.                |
| `tests/Passless.IntegrationTests` | xUnit and Testcontainers.                      |
| `tests/Passless.E2ETests`         | Playwright and the CDP virtual authenticator.  |
| `client/`                         | Angular 21, standalone, zoneless, Tailwind.    |
| `docs/`                           | Design, threat model, ADRs.                    |
| `scripts/`                        | Local development helpers.                     |

## Non-goals

Deliberately absent, so that what is here can be done properly:

- An OAuth 2.0 or OIDC provider surface. This issues its own tokens to its own
  client.
- Multi-tenancy.
- Attestation-based device allowlisting. Attestation is verified where it is
  offered, but no enterprise policy engine sits on top of it.
- Account recovery beyond what is needed to make the passkey story honest. Real
  recovery is a product decision, not a protocol one.

## Licence

MIT. See [LICENSE](LICENSE).
