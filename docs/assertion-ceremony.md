# Assertion ceremony

How `POST /login/options` and `POST /login/verify` fit together, and where each
security control sits.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Browser
    participant Auth as Authenticator
    participant API as Passless API
    participant Redis
    participant DB as PostgreSQL

    User->>Browser: Sign in
    Browser->>API: POST /login/options

    alt username supplied and account exists
        API->>DB: load credential descriptors
        DB-->>API: descriptors
    else username supplied and unknown
        Note over API: derive decoy descriptors<br/>HMAC(key, normalized username)
    else no username
        Note over API: empty allowCredentials<br/>discoverable credentials only
    end

    API->>Redis: SET challenge EX ttl NX
    API-->>Browser: options + Set-Cookie ceremony handle
    Note over API,Browser: response shape identical<br/>whether or not the account exists

    Browser->>Auth: navigator.credentials.get()
    Auth->>User: verify presence
    User-->>Auth: biometric or PIN
    Auth-->>Browser: signed assertion + userHandle
    Browser->>API: POST /login/verify

    API->>Redis: GETDEL challenge
    Redis-->>API: ticket or nil
    Note over API,Redis: atomic, so a replay<br/>and a concurrent submit<br/>both find it gone

    alt ticket missing
        API-->>Browser: 400 authentication_failed
    else ticket found
        API->>API: check clientData type and origin
        API->>DB: find credential by id
        API->>API: verify signature, RP ID hash, user presence
        Note over API: counter check runs only after<br/>the signature verifies, so a<br/>clone alarm cannot be forged

        alt stored counter > 0 and presented <= stored
            API->>DB: audit SignCounterRegression (critical)
            API-->>Browser: 400 authentication_failed
        else counter acceptable
            API->>DB: update last used, create Session + TokenFamily, audit
            API-->>Browser: 200 sessionId
        end
    end
```

## Notes on the diagram

**Decoys.** Asking for options with a username that does not exist returns
derived descriptors rather than an empty list, so the response cannot be used to
test whether an account exists. Derived rather than random, so asking twice does
not separate the invented from the real.

**Atomic consumption.** The challenge is fetched and destroyed by a single
`GETDEL`. A read followed by a delete would let two concurrent submissions both
observe the ticket.

**Order of the counter check.** The signature is verified first, deliberately.
If the counter rule ran first, anyone could post an unsigned response with a low
counter and make the server raise a critical "possible cloned authenticator"
event about a credential they do not hold. An alarm anybody can trigger is not
an alarm.

**Counter zero.** A credential whose stored counter is zero is never held to a
counter. Most synced passkey providers never increment one, because the
credential is designed to live on several devices at once; a strict monotonic
rule would lock out the common case and catch nobody.

**One response for every failure.** A stale challenge, an unknown credential, a
disabled account and a cloned authenticator all return the same body. The
distinction is in the audit log.
