<!-- marina -->
# Design 001 - Server-Driven Authentication

**Inspired by Airbnb's authentication architecture redesign**

Original article: [Airbnb Cuts Authentication Code by 60% with Server Driven Architecture (InfoQ)](https://www.infoq.com/news/2026/09/airbnb-server-driven-login/)

> **Status: In Progress.** This design is currently documentation only. Everything under "Planned Implementation" and "Hands-On Experiments" describes work that is **not yet implemented**.
>
> This is an independent educational interpretation based on the publicly available article. It is not a copy of Airbnb's system, and it makes no claims about Airbnb's internal code.

## 1. The Problem

In a client-driven authentication model, each client (Web, iOS, Android) contains its own logic for deciding which login step to show: password, one-time code, authenticator app, or an alternative method. This leads to:

- **Duplicated authentication-flow logic.** The same rules are implemented three times, in three languages and frameworks.
- **Client deployments for auth changes.** Changing the preferred method for a region or user segment requires shipping new client versions. Mobile releases add app-store review and slow user adoption of updates.
- **Inconsistent behavior across platforms.** Implementations drift, so the same user can see different flows on different devices.
- **Difficult experiments.** Running an authentication experiment means coordinating changes and releases across every platform.

## 2. Before - Client-Driven Authentication

![Before - Client-Driven Authentication](docs/Before%20Client-Driven%20Authentication%20Architecture.png)

In this model, every client owns its "Login Decision Logic" ("Show password? Show OTP? Show TOTP? Try another way?"). The Authentication API mainly verifies credentials and reads and writes user and account data. The decisions about *what to ask the user next* live in the clients.

This becomes hard to maintain because:

- A single rule change must be implemented, tested, and released on each platform.
- Platforms can disagree on edge cases, producing inconsistent user experiences.
- Experiments roll out slowly, since each variant needs client changes.

## 3. The Solution

In **server-driven authentication**, the backend decides which authentication challenge to present, and clients render what the server tells them to.

Responsibilities:

| Component | Responsibility |
|---|---|
| **Authentication API** | Exposes the flow to clients. Returns the next step (the selected challenge and alternatives) and accepts the user's response. |
| **Policy Engine** | Chooses the authentication challenge based on user, account, and session context, configured rules, available methods, and feature flags. |
| **Clients** | Render the step returned by the server, submit user input, and display "Try another way" options. They do not decide the flow. |

The article describes this as an identify-then-challenge flow. The user first identifies the account. The server then evaluates context through a configurable policy engine and selects the challenge most likely to succeed, returning other supported methods as fallbacks.

## 4. After - Server-Driven Authentication

![After - Server-Driven Authentication](docs/After_%20Server-Driven%20Authentication.png)

Login-flow decisions move to the backend:

1. A client sends the user's input to the Authentication API.
2. The API asks the Policy Engine for the next step.
3. The Policy Engine consults inputs such as user/account data, policy rules (for example risk or region), experiments and feature flags, and available authentication methods.
4. The API returns the selected method (for example, Email OTP) plus alternatives (for example, Password and TOTP).
5. The client's UI renderer displays the selected step and the "Try another way" options.

Changing the preferred method or running an experiment becomes a server-side change rather than a client release. The diagram is our simplified model of the approach. Component names and details are our own, not Airbnb's.

## 5. Architecture Concepts and Design Patterns

### What the article describes

The source article describes: a server-side configurable policy engine that selects the challenge, clients that render server-returned screens, ranked fallback options ("Try another way"), and server-side schemas that generate client type definitions. It does **not** describe code-level design patterns. The patterns below are **our own implementation choices** for this educational project.

### Concepts

**Server-Driven UI** (described in the article)
The server returns what to display and the client renders it. It is relevant because it keeps flow decisions out of the clients. In our design, the API will return a step description (for example, a challenge type and a list of alternatives) that the Angular app renders.

**Policy Engine** (described in the article)
A component that evaluates rules and context to produce a decision. In our design it will pick the preferred authentication method from user attributes, region, feature flags, and available methods.

**Strategy Pattern** (our choice)
Each authentication method (Password, Email OTP, TOTP) will be a separate implementation of a common interface. This makes it straightforward to add a new method without changing existing ones. We are not claiming Airbnb uses this pattern.

**Separation of Concerns** (our choice)
Decision-making (policy), verification (strategies), transport (API), and presentation (Angular) will be kept in distinct components so each can change and be tested independently.

**Dependency Injection** (our choice)
Policy engine, strategies, and configuration will be supplied through ASP.NET Core's built-in DI container. This enables swapping in test doubles, registering new strategies, and simulating failures in tests.

## 5a. Backend Implementation (Phase 2)

> **Update:** the ASP.NET Core backend (policy engine, strategies, API, tests) is implemented, and an Angular client is described in section 5b. The "Planned Implementation" and "Hands-On Experiments" sections below still describe future work except where stated in 5a and 5b.
>
> **This is a simulation.** It selects which challenge to show. It verifies no credentials, issues no tokens, creates no sessions, and never reports a user as authenticated.

### Components implemented

All code lives under `backend/`.

| Component | Location | Responsibility |
|---|---|---|
| `IAuthenticationStrategy` + `PasswordStrategy`, `EmailOtpStrategy`, `TotpStrategy` | `src/AuthPolicyEngine.Api/Strategies` | Each method reports its type, display info, and whether it applies to an account. No verification logic. |
| `IAuthenticationPolicyEngine` / `AuthenticationPolicyEngine` | `.../Policy` | Filters applicable methods, orders them by server-side config, returns the preferred challenge plus alternatives. |
| `AuthPolicyOptions` | `.../Policy` | Config model (`AuthPolicyOptions`, section `AuthPolicy`). |
| `InMemoryAccountRepository` | `.../Accounts` | Fictional demo accounts. |
| `InMemoryAuthFlowStore` | `.../Flows` | Server-held flow state (allowed methods, 10-minute expiry). |
| `AuthController` | `.../Controllers` | The two endpoints below. |
| Tests | `tests/AuthPolicyEngine.Tests` | xUnit unit tests and `WebApplicationFactory` integration tests. |

### Design patterns used

- **Policy Engine**: one place decides which challenge is selected, from account data, request context, and configuration.
- **Strategy**: each authentication method is a separate `IAuthenticationStrategy` registered in DI. Adding a method means adding a class and a registration.
- **Dependency Injection** and **Options pattern** (`IOptionsMonitor`) for strategies, the engine, and live-reloadable policy.

### Demo accounts

All data is fictional. There are no passwords, secrets, or real credentials.

| Account | Identifier | Eligible methods |
|---|---|---|
| A | `account.a@example.test` | Password, Email OTP |
| B | `account.b@example.test` | Password, TOTP |
| C | `account.c@example.test` | Email OTP only |
| D | `account.d@example.test` | None |

### API

Both endpoints return `application/json`. Errors use RFC 7807 `application/problem+json`.

**`POST /api/auth/identify`**

Request: `{ "identifier": "account.a@example.test", "region": "EU" }` (`region` is optional).

Response `200`:

```json
{
  "flowId": "3f1c...",
  "expiresAt": "2026-01-01T00:10:00+00:00",
  "selectedChallenge": { "type": "Password", "displayName": "Password", "description": "Enter your account password." },
  "alternatives": [
    { "type": "EmailOtp", "displayName": "Email code", "description": "Enter a one-time code sent to your email." }
  ],
  "notice": "SIMULATION: challenge selection only. ..."
}
```

For account D, the response is `200` with `flowId`, `expiresAt`, and `selectedChallenge` all `null` and `alternatives` empty.

| Status | Meaning |
|---|---|
| 200 | Policy decision returned (possibly with no challenge). |
| 400 | Missing, empty, or malformed body or identifier. |
| 404 | Unknown account. |

**`POST /api/auth/flows/{flowId}/select`**

Request: `{ "authenticationType": "EmailOtp" }`. Valid values: `Password`, `EmailOtp`, `Totp`. Integers are rejected.

Response `200` has the same shape as identify, with the requested method as `selectedChallenge` and the rest as alternatives.

| Status | Meaning |
|---|---|
| 400 | Missing or invalid `authenticationType`, or malformed body. |
| 404 | Unknown flow. |
| 409 | Method is not in the server's allowed list for this flow. The client cannot authorize arbitrary methods. |
| 410 | Flow expired (flows live 10 minutes from `identify`; selecting does not extend them). |

### Build, run, test

Requires the .NET 10 SDK.

```powershell
cd Design-001-Server-Driven-Authentication\backend
dotnet build
dotnet run --project src\AuthPolicyEngine.Api --urls http://localhost:5199
dotnet test
```

### Sample requests

```powershell
Invoke-RestMethod -Method Post http://localhost:5199/api/auth/identify `
  -ContentType application/json -Body '{"identifier":"account.a@example.test"}'

Invoke-RestMethod -Method Post http://localhost:5199/api/auth/flows/<flowId>/select `
  -ContentType application/json -Body '{"authenticationType":"EmailOtp"}'
```

### Changing the authentication policy

Edit `backend/src/AuthPolicyEngine.Api/appsettings.json`:

```json
"AuthPolicy": {
  "PreferredOrder": [ "Password", "EmailOtp", "Totp" ],
  "RegionOverrides": { "EU": [ "Totp", "EmailOtp", "Password" ] },
  "FlowLifetime": "00:10:00"
}
```

- `PreferredOrder` is most-preferred first. The engine picks the first method the account actually has, and lists the remaining applicable methods as alternatives in order. Methods left out of the list are appended, so an account's available methods are never dropped.
- `RegionOverrides` replaces the order when the request's `region` matches (case-insensitive).
- **Live reload:** the running app picks up saved changes to `appsettings.json` without a restart. This was verified by changing the preferred method while the server ran. Flows already created keep their original allowed methods. `FlowLifetime` applies to flows created after the change.

### Known limitations

- Simulation only: no credential verification, tokens, sessions, or "authenticated" state.
- Data and flows are in memory and are lost on restart. Flows are not shared across instances.
- **Account enumeration is not prevented.** `identify` returns `404` for unknown accounts and a distinct response for accounts with no methods, so an attacker can discover which identifiers exist. A production system must return indistinguishable responses and protect this endpoint.
- Also missing for production: secure credential validation, rate limiting and lockout, replay protection and single-use flow handling, auditing and logging, HTTPS and CSRF/CORS hardening, and secure flow-ID handling. Expiry here is a simple fixed 10 minutes.
- Angular client: see section 5b.
- Strategy execution and challenge delivery (sending emails, validating codes) are out of scope.

## 5b. Angular Client (Phase 3)

> **Still a simulation.** The client shows which challenge the server selected. It collects no credentials, holds no tokens or sessions, and never says a user is signed in.

Code lives under `frontend/` (Angular 22, standalone components, `HttpClient`, Vitest via `ng test`; no extra runtime dependencies).

### How it works

The client contains no policy. It never decides priority, never looks at account properties, and has no preferred order. It only does this:

1. `POST /api/auth/identify` with the identifier and optional region.
2. Renders `selectedChallenge` and the `alternatives` array exactly as returned, in the server's order.
3. On "Try another way", `POST /api/auth/flows/{flowId}/select` and renders the new response.

Structure:

| Piece | File | Role |
|---|---|---|
| Contracts | `src/app/models/auth.models.ts` | Types mirroring the backend DTOs. Challenge `type` is a string so unknown future types do not break the client. |
| `AuthApiService` | `src/app/services/auth-api.service.ts` | Typed HTTP calls, response validation, HTTP-status to message mapping. |
| `IdentifyFormComponent` | `src/app/components/` | Identifier, region, demo account shortcuts A-D (these only fill the input). |
| `ChallengePanelComponent` | `src/app/components/` | Selected challenge, alternatives, expiry countdown, no-methods state. |
| `App` | `src/app/app.ts` | Holds the current server response and error. |

Graceful handling: a challenge type the client has no look for (for example a future `Passkey`) is still displayed using the server's text with a warning; unsupported alternatives are shown but disabled. Malformed `200` responses are rejected with an error rather than rendered. Errors handled: unknown account (404), invalid request (400), unknown flow (404 on select), disallowed method (409), expired flow (410), backend unreachable, unexpected responses. Expired or unknown flows offer "Start new flow". The expiry countdown uses the browser clock; the server remains the authority and answers `410` once the flow expires.

### Run backend and frontend together

Requires Node.js 20+ (tested on 24) and the .NET 10 SDK. One-time: `dotnet dev-certs https --trust`.

```powershell
# Terminal 1 - backend (HTTPS, dev certificate)
cd Design-001-Server-Driven-Authentication\backend
dotnet run --project src\AuthPolicyEngine.Api --urls https://localhost:5001

# Terminal 2 - frontend
cd Design-001-Server-Driven-Authentication\frontend
npm install
npm start
```

Open `http://localhost:4200`. Pick a demo account (A to D, see 5a) and press Continue.

### API integration

Angular calls relative `/api/...` URLs. `npm start` runs the dev server with `proxy.conf.js`, which forwards `/api` to `https://localhost:5001`, so no CORS change was made to the backend. TLS verification stays on: `npm start` runs Node with `--use-system-ca` so it trusts the ASP.NET dev certificate once it is trusted in the OS store. To use another backend address set `API_TARGET` before `npm start` (for example `$env:API_TARGET = "https://localhost:7001"`); no code changes are needed. The proxy is for development only. A deployed setup needs a reverse proxy or a CORS policy.

### Live policy change experiment

1. Start the backend and `npm start` as above, open `http://localhost:4200`.
2. Identify **Account A**. With `PreferredOrder` of `Password, EmailOtp, Totp`, the server selects **Password**.
3. In `backend/src/AuthPolicyEngine.Api/appsettings.json`, change `PreferredOrder` to `[ "EmailOtp", "Password", "Totp" ]` and save. Do not restart anything.
4. In the UI choose "Start over", identify Account A again. It now shows **Email code**, with Password as the alternative.
5. Revert the file when done.

This was run against the live servers (through the Angular dev-server proxy, with the Angular app untouched): Password before the edit, EmailOtp after, Password after the revert. The browser UI itself was not driven by an automated browser test; UI rendering is covered by the unit tests below.

### Run tests

```powershell
cd Design-001-Server-Driven-Authentication\frontend
npm run build
npm test -- --watch=false      # 17 tests, HTTP mocked, no backend needed

cd ..\backend
dotnet test                    # 26 backend tests
```

### Frontend limitations

- No real authentication: no credential input, verification, tokens, or sessions.
- No end-to-end browser tests; component tests mock HTTP.
- The dev proxy and `--use-system-ca` setup is for local development only.
- A new flow is the only way to pick up a changed server policy; open flows keep their original allowed methods.
- The icon set only knows Password, EmailOtp and Totp; other types use a generic look.
- Account enumeration (see 5a) is visible in the UI: unknown accounts show a distinct message.

## 6. Planned Implementation

Planned technology stack:

- C# / ASP.NET Core Web API
- Angular
- Simulated Password, Email OTP, and TOTP authentication
- Automated tests

This will be a small educational demonstration, **not a production authentication service**. Credentials, codes, and sessions will be simulated, and the project will not be hardened for real-world use.

## 7. Hands-On Experiments

Planned experiments (none implemented yet):

1. Change the preferred authentication method without changing Angular code.
2. Add a new authentication strategy.
3. Select different flows based on server-side policy.
4. Demonstrate feature flags.
5. Simulate policy-engine failure.
6. Test compatibility when clients do not support a selected challenge.

## 8. Architectural Tradeoffs

**Benefits**

- Authentication rules are centralized in one place.
- Flow changes and experiments do not require client releases.
- Behavior is more consistent across platforms.
- Less duplicated logic in clients.

**Limitations and risks**

- **Backend availability.** Clients depend on the server to know what to show. A policy-engine or API outage can block login, so a safe fallback is needed.
- **API contract compatibility.** Older clients may not understand a new challenge type. The contract needs versioning, capability negotiation, or a defined fallback.
- **Central policy management.** One misconfigured rule affects all platforms at once, so policy changes need review, testing, and safe rollout.
- **Security enforcement.** The server must verify every step and never trust the client to have followed the intended flow. Server-driven UI is a presentation concern, not a security control.

## 9. Learning Goals

After completing this project, a reader should be able to:

- Explain the difference between client-driven and server-driven authentication flows.
- Describe the roles of the authentication API, policy engine, and clients.
- Apply the Strategy Pattern and Dependency Injection to make authentication methods pluggable.
- Reason about compatibility, availability, and security tradeoffs of server-driven designs.
- Design and test experiments that change behavior through server-side policy.

## 10. References

- [Airbnb Cuts Authentication Code by 60% with Server Driven Architecture - InfoQ, Sep 2026](https://www.infoq.com/news/2026/09/airbnb-server-driven-login/)
<!-- end marina -->
