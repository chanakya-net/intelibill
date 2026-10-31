# Phone verification: manual setup

Phone sign-in is off unless a deployment turns it on. With `PhoneAuth:Enabled` unset or `false`, `POST /api/auth/register/phone` and every `/api/auth/phone/*` route answer `503 Auth.PhoneEnrollmentUnavailable`, and the web client hides the option. Turn it on only after the rehearsal at the bottom of this page.

The legacy `register/phone` route stays closed either way. It granted a session on a number nobody had proved, which left the registrant unable to sign in again once the session was gone and let anyone reserve someone else's number. Accounts it created keep their data and their sessions.

## 1. Choose a delivery transport

### Local capture: nothing leaves the machine

The default for development and for every automated test. The code is rendered, encrypted into the `identity_sms` outbox row, and written to the application log by `LocalCaptureSmsSender` — the one place a code is ever logged, which is why `PhoneAuthOptionsValidator` refuses this setting outside `Development` and `Testing`.

| Configuration | Value |
|---|---|
| `PhoneAuth__Enabled` | `true` |
| `PhoneAuth__LocalCapture` | `true` |
| `Identity__PhoneCodeKey` | base64 32-byte key, supplied as a secret |
| `Identity__OutboxKey` | base64 32-byte key, supplied as a secret (shared with identity mail) |

Generate either key with:

```bash
openssl rand -base64 32
```

Use `:` instead of `__` when setting keys through .NET user secrets. Integration tests read the code straight out of the encrypted row; they never need a transport.

### Twilio trial: delivery to an actual handset, free

A Twilio trial account sends real SMS on free trial credit with no card required to start. Checked 1 November 2026:

- It delivers only to **verified recipient numbers**, up to 5, and the number you signed up with is verified automatically. Trial accounts verify recipients by SMS only, and expire 30 days after sign-up.
- Message bodies arrive prefixed with "Sent from your Twilio trial account".
- **India needs no DLT registration on the international gateway.** That is what makes a trial able to reach an Indian handset today. In exchange any sender id is replaced with a random 5-to-9-digit short code, the message bypasses the Indian DND registry, and shortened URLs are not allowed — which is why the code body carries no link.
- The **domestic** gateway is the production route and does require completed TRAI DLT entity, header and template registration on the operator portal before Twilio will whitelist the sender; the entity id and template id then travel on every submission.

| Configuration | Value |
|---|---|
| `PhoneAuth__Enabled` | `true` |
| `PhoneAuth__LocalCapture` | `false` |
| `PhoneAuth__Provider` | `Twilio` |
| `PhoneAuth__AccountSid` | Account SID, supplied as a secret |
| `PhoneAuth__AuthToken` | Auth token, supplied as a secret |
| `PhoneAuth__FromNumber` | Your Twilio number, or set `PhoneAuth__MessagingServiceSid` instead |
| `PhoneAuth__DltEntityId` | Empty for the international gateway; the registered principal entity id for the domestic one |
| `PhoneAuth__DltTemplateId` | Empty, or the DLT-approved template matching the body this application sends |

The two DLT values must be set together or both left empty; the startup validator refuses one without the other, because one alone is neither route.

### Twilio test credentials: the REST API without delivery

A separate credential pair that exercises the API without charging the account or reaching a handset, with magic numbers that force specific outcomes — `+15005550006` passes validation, `+15005550001` is an invalid number, `+15005550009` cannot receive SMS. Useful for transport-level tests of retry and error mapping. Free.

### If the trial's verified-recipient cap gets in the way

An Android SMS gateway such as [textbee](https://textbee.dev/) turns a handset you already own into the transport over your own SIM. Because the traffic is person-to-person it needs no DLT, and it speaks a plain HTTP API, so it fits the same `ISmsSender` seam. Its advertised free limits (50 a day, 300 a month) are a vendor claim.

### What is not free

Production OTP traffic to Indian numbers over the domestic route needs DLT registration and per-message spend. Indian vendors advertise signup credits — Fast2SMS ₹50, Authkey's startup plan ₹2,500 for 10k OTPs on approval, trial packs from MSG91 and 2Factor — but those are trial credit rather than a free tier, and none of them exempts you from DLT. Vendor choice, the message-cost ceiling and supported countries are open product decisions; the code commits only to the `ISmsSender` seam and one Twilio implementation.

## 2. Code policy

Defaults, all overridable, all range-checked at startup:

| Configuration | Default | Meaning |
|---|---|---|
| `PhoneAuth__CodeLength` | `6` | Digits in a code |
| `PhoneAuth__CodeLifetimeMinutes` | `5` | How long a code can be presented |
| `PhoneAuth__MaxAttempts` | `5` | Wrong guesses before the challenge closes |
| `PhoneAuth__ResendSpacingSeconds` | `60` | Wait before a replacement code |
| `PhoneAuth__DestinationSendsPerHour` | `5` | Codes per number per hour |
| `PhoneAuth__DefaultCountryCode` | `+91` | The country a bare national number may be read against; empty requires international format |

A code is stored as a keyed HMAC-SHA256 under `Identity:PhoneCodeKey`, never as a bare digest: six digits is a million possibilities, so a plain hash column is a plaintext column with extra steps for anyone holding a database dump. The key id is derived from the key, so rotating the secret stops old challenges matching rather than comparing them against the wrong key; they live five minutes, and the holder asks for a new code.

`PhoneAuth:DefaultCountryCode` does not make an ambiguous number acceptable. `PhoneNumberFormat` takes an international number as given and a bare number only against a national rule written in that file; `0987654321` is refused rather than read as either `+910987654321` or `+919876543210`. Adding a country means adding its rule there.

## 3. Migration

```bash
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

`AddVerifiedPhoneChallenges` adds `phone_challenges`, `identity_sms`, and `users.is_phone_verified` / `users.phone_verified_at`. The backfill value for `is_phone_verified` is `false`, because unknown is not verified: every number that predates verification stays contact information until its holder proves it. Deployed environments apply this through the migration job; API startup does not.

## 4. Before turning it on

`PhoneAuth:Enabled` stays `false` on a deployed environment until all of these are done and recorded:

1. One controlled real-delivery rehearsal on a Twilio trial, covering sign-up, sign-out, a second device, sign-in, and lost-session recovery.
2. A review of the provider, template and consent configuration, including the DLT route the deployment will actually use.
3. Destination, IP and attempt budgets in place beyond the per-destination budget this feature enforces itself.

Partial OTP enrollment on its own is not evidence for release: a signup that works and then strands its user is the problem this feature exists to remove.

## 5. Rolling back

Set `PhoneAuth:Enabled=false`. Existing sessions continue, assisted recovery through [verified email](auth-email-setup.md) continues, and consumed-proof rows are retained for audit. No account is deleted and nobody is signed out by the switch.
