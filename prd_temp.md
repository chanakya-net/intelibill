## Problem Statement

Users of the Intelibill mobile application currently lack the ability to update their profile information, change their password, and switch their active shop context from within the app.

## Solution

Introduce a "Profile & Settings" feature accessed via a Top App Bar icon. This feature will provide an overview screen where users can switch their active shop via a dropdown. It will also provide navigation to dedicated sub-screens for updating their profile (First Name, Last Name, Email, Phone Number) and changing their password. The app will seamlessly handle state invalidation (via Riverpod) to reflect changes across the app when the active shop or profile is updated.

## User Stories

1. As a user, I want to access my profile settings from the main application shell, so that I can easily manage my account.
2. As a user with multiple shops, I want to switch my active shop, so that I can manage inventory and sales for different locations.
3. As a user, I want to update my profile information (Name, Email, Phone), so that my details remain current.
4. As a user, I want to change my password, so that I can maintain the security of my account.

## Implementation Decisions

- **Modules to build or modify**:
  - `lib/src/features/auth/data/dto/`: Add DTOs for profile update, password change, and shop switching.
  - `lib/src/features/auth/data/data_sources/auth_remote_data_source.dart`: Add API calls for the new endpoints.
  - `lib/src/features/auth/domain/repositories/auth_repository.dart`: Add repository methods for the features.
  - `lib/src/features/auth/presentation/controllers/auth_controller.dart`: Add Riverpod controller methods to coordinate the flow and update the `AuthSession` state.
  - `lib/src/app/router/app_router.dart`: Add routes for `/profile`, `/profile/edit`, `/profile/change-password`.
  - `lib/src/app/shell/mobile_app_shell.dart`: Add a profile icon to the Top App Bar.
  - `lib/src/features/auth/presentation/pages/`: Create new UI pages for the settings overview, edit profile, and change password.
- **Interfaces likely to change**:
  - `AuthRemoteDataSource`
  - `AuthRepository`
- **Technical clarifications**:
  - Shop switching and profile updates return a new `AuthResult` (JWTs). The tokens must be persisted in secure storage, and the `AuthController` state must be updated to trigger Riverpod's reactive data reloading.
- **Architectural decisions**:
  - The Profile & Settings will use an "Overview with Sub-screens" approach.

## Testing Decisions

- **What makes a good test**: Tests should verify that the `AuthController` correctly handles the API responses, updates the state with the new session, and properly persists the new tokens. UI tests should ensure the forms submit correctly and the shop dropdown triggers the switch logic.
- **Which modules will be tested**: `AuthController` (unit tests).
- **Prior art in the codebase**: Existing auth controller tests should serve as a template.

## Out of Scope

- Managing users within a shop (adding/editing other staff).
- Creating a new shop from the mobile app.
- Changing the application language (remains separate).

## Further Notes

- Riverpod code generation (`build_runner`) must be run after creating the new DTOs and Controller methods.