# intelibill_mobile

A new Flutter project.

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.

## Local Backend & Mobile Testing

### Backend Startup

Start the local ASP.NET Core API backend:

```bash
cd <path-to-repo>/intelibill
dotnet run --project src/backend/Intelibill.Api
```

The backend will be available at `http://localhost:5277/api`.

### Test User Registration

Register a test user via the email endpoint:

```bash
curl -X POST http://localhost:5277/api/auth/register/email \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "Password123!",
    "shopName": "Test Shop"
  }'
```

### Android Emulator

The emulator default API base URL is configured to use `http://10.0.2.2:5277/api` (this is the Android emulator's standard way to reach the host machine's localhost).

To run on the emulator:

```bash
flutter run
```

### Physical Device Testing

For physical devices, the emulator special address `10.0.2.2` does not apply. Instead, use your machine's LAN IP address:

```bash
# Replace <machine-lan-ip> with your local machine's IP (example: 192.168.1.100)
flutter run --dart-define=API_BASE_URL=http://<machine-lan-ip>:5277/api
```

To find your machine's LAN IP:
- **macOS/Linux**: `ifconfig | grep "inet " | grep -v 127.0.0.1`
- **Windows**: `ipconfig`

### Dependency Management

Update dependencies with:

```bash
flutter pub get
dart run build_runner build --delete-conflicting-outputs
```

Check for outdated packages:

```bash
flutter pub outdated
```

Lint and format code:

```bash
flutter analyze
dart format --set-exit-if-changed .
flutter test --coverage
```
