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
