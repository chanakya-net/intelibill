# Email Or Mobile Login Technical Requirements

## Summary

Allow users to sign in with either email address or mobile number in the same login field, plus password. The current login flow is email-only on both frontend and backend. The new flow must introduce a generic login identifier while preserving the existing email login endpoint for compatibility.

## Resolved Decisions

- The login page has one identifier field where the user can enter either email or mobile number.
- Phone login uses exact stored phone-number matching after trimming leading/trailing whitespace.
- No country-specific normalization is required.
- No automatic conversion from `9876543210` to `+919876543210`.
- If a user enters a phone number that does not exactly match the stored `users.phone_number`, login fails with the generic invalid credentials response.
- Identifiers containing `@` use the email path.
- Identifiers without `@` use the phone path.
- Email identifiers are trimmed and lowercased before lookup.
- Phone identifiers are trimmed only.
- Do not remove internal spaces or other internal characters from either identifier type.
- Empty identifiers are validation errors.
- Non-empty identifiers should not be rejected because of phone format.
- Malformed or unknown non-empty identifiers should flow to lookup/password verification and return generic invalid credentials on failure.
- Phone/password login only succeeds for users with an existing `PasswordHash`.
- Phone-only users created without a password hash return generic invalid credentials when attempting password login.
- Shop-created users with stored phone numbers and passwords must be able to log in with exact stored phone plus password.
- Login must continue to respect `IsLoginEnabled`.
- The existing filtered unique index on `users.phone_number` is sufficient; no duplicate-resolution behavior is required.
- No DB migration is expected. Add one only if implementation discovers a real schema requirement.
- Rate limiting must apply to both generic and legacy login endpoints.

## Backend Requirements

### API Contract

Add a new endpoint:

```http
POST /api/auth/login
```

Request:

```csharp
public sealed record LoginRequest(string Identifier, string Password);
```

Response:

- Same `AuthResult` payload as the current email login.
- `200 OK` on success.
- `400 Bad Request` for empty identifier/password validation failures.
- `401 Unauthorized` with `Auth.InvalidCredentials` for unknown identifier, wrong password, missing password hash, or non-matching phone.
- `401 Unauthorized` with `Auth.UserLoginDisabled` when matching user login is disabled.

### Legacy Compatibility

Keep:

```http
POST /api/auth/login/email
```

The legacy endpoint should dispatch to the same generic login command. Its existing `email` request field should be treated as the generic identifier for compatibility:

```csharp
public sealed record LoginWithEmailRequest(string Email, string Password);
```

New clients should use `POST /api/auth/login`.

### Application Layer

Add new generic login command types:

```csharp
public sealed record LoginCommand(string Identifier, string Password);
```

Recommended command behavior:

1. Trim `Identifier`.
2. If trimmed identifier is empty, return validation error.
3. If identifier contains `@`:
   - Lowercase it.
   - Lookup via `IUserRepository.GetByEmailAsync`.
4. Otherwise:
   - Lookup via `IUserRepository.GetByPhoneAsync` using the exact trimmed value.
5. If user is missing, `PasswordHash` is null, or password verification fails, return `Errors.Auth.InvalidCredentials`.
6. If `IsLoginEnabled` is false, return `Errors.Auth.UserLoginDisabled`.
7. Reuse the current token, refresh token, active shop selection, and `AuthResult` behavior.

### Repository Requirements

Existing repository methods are sufficient:

- `GetByEmailAsync(string email, CancellationToken cancellationToken = default)`
- `GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default)`

`GetByPhoneAsync` must include the same details needed by login token creation, especially shop memberships and shops. Current `GetByEmailAsync` includes these details; phone lookup must be equivalent for login.

### Rate Limiting

Apply the current login rate limit to both endpoints:

- Limit: 10 attempts.
- Period: 1 minute.
- Backoff: 3 minutes.

## Frontend Requirements

### Login Page

Replace the email-specific login form control with a generic identifier concept.

UI copy:

- Label: `Email or mobile number`
- Placeholder: `Enter email or mobile number`
- Validation: `Enter your email or mobile number.`

Frontend validation:

- Required only.
- Remove Angular `Validators.email` from the combined identifier field.
- Trim leading/trailing whitespace before submit.
- Do not alter internal characters.

Submit behavior:

- Call a generic auth service method such as `login(identifier, password, rememberMe)`.
- Send payload `{ identifier, password }`.
- Use the new endpoint constant for `POST /api/auth/login`.

### Remember Me

Rename the concept from last email to last login identifier.

Required behavior:

- Remember the trimmed identifier when `rememberMe` is true.
- Remember either email or phone values.
- Clear the remembered identifier when `rememberMe` is false.
- Prefill the login field with the last remembered identifier, regardless of whether it is email or phone.
- Read the old storage key `inventory.auth.last-email` as a migration fallback.
- Save future values to a new key such as `inventory.auth.last-identifier`.

## Data Requirements

No planned schema change.

Existing columns:

- `users.email`
- `users.phone_number`
- `users.password_hash`
- `users.is_login_enabled`

Existing filtered unique indexes on email and phone number should remain the source of uniqueness.

## Security And Error Handling

- Do not reveal whether a phone number exists.
- Do not reveal whether an account has no password hash.
- Preserve generic invalid credentials behavior for lookup and password failures.
- Keep disabled-login behavior unchanged.
- Preserve rate limiting on login attempts.
- Avoid adding phone-format-specific error messages that could create inconsistent client/server behavior.

## Test Coverage Requirements

The implementation should add high coverage for positive and negative cases.

Backend unit tests:

- Email identifier success.
- Email lookup remains case-insensitive.
- Phone identifier success with exact stored phone value.
- Phone identifier does not normalize.
- Phone identifier with non-matching format returns invalid credentials.
- Unknown email returns invalid credentials.
- Unknown phone returns invalid credentials.
- Wrong password returns invalid credentials.
- Matching user with null password hash returns invalid credentials.
- Disabled matching user returns user-login-disabled error.
- Identifier is trimmed before lookup.
- Phone internal characters are not changed.
- Email internal characters are not changed except lowercasing.

Validator tests:

- Empty identifier is rejected.
- Whitespace-only identifier is rejected.
- Empty password is rejected.
- Non-empty email-like identifier is accepted by command validation.
- Non-empty phone-like identifier is accepted by command validation.
- No phone format normalization is asserted in validation.

API controller tests:

- `POST /api/auth/login` dispatches `LoginCommand`.
- `POST /api/auth/login` returns `200 OK` on success.
- `POST /api/auth/login` maps invalid credentials to `401`.
- `POST /api/auth/login` maps validation failure to `400`.
- Legacy `POST /api/auth/login/email` dispatches the same generic command.
- Legacy endpoint remains successful for email credentials.
- Legacy endpoint accepts its `email` field as the identifier.

Integration tests:

- Registered email user can log in via `POST /api/auth/login`.
- Existing legacy email login still works.
- Shop-created user with phone number and password can log in via exact phone identifier.
- Exact phone mismatch returns `401`.
- Phone-only user with no password hash cannot password-login and receives `401`.
- Disabled user cannot login by email or phone.
- Login rate limit still applies to the generic endpoint.

Frontend unit tests:

- Login form accepts phone input.
- Login form no longer rejects non-email input solely because it is not an email.
- Empty identifier blocks submit and shows the new validation copy.
- Submit trims identifier before calling auth service.
- Auth service posts `{ identifier, password }` to `AUTH_ENDPOINTS.login`.
- Auth service stores trimmed remembered identifier when `rememberMe` is true.
- Auth service clears remembered identifier when `rememberMe` is false.
- Login page pre-fills remembered phone identifier.
- Login page pre-fills remembered email identifier.
- Storage reads old last-email key as fallback.
- Storage writes new last-identifier key for future saves.

## Out Of Scope

- Phone OTP login.
- Password setup or reset by phone number.
- Country detection.
- Phone number normalization.
- Username login.
- DB schema redesign.
- Removing the legacy `login/email` endpoint.
