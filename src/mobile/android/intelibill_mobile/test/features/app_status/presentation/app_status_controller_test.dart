import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/use_cases/get_app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetAppStatus extends Mock implements GetAppStatus {}

void main() {
  late MockGetAppStatus getAppStatus;

  setUp(() {
    getAppStatus = MockGetAppStatus();
  });

  group('AppStatusController', () {
    test('loads status through use case provider', () async {
      final expectedStatus = AppStatus(
        statusText: 'Ready',
        apiBaseUrl: 'https://api.example.com',
        timestamp: DateTime.utc(2026, 5, 14, 10),
        environment: 'test',
      );
      when(() => getAppStatus()).thenAnswer((_) async => expectedStatus);

      final container = ProviderContainer(
        overrides: [
          getAppStatusUseCaseProvider.overrideWithValue(getAppStatus),
        ],
      );
      addTearDown(container.dispose);

      final result = await container.read(appStatusControllerProvider.future);

      expect(result, expectedStatus);
      verify(() => getAppStatus()).called(1);
    });

    test('refresh reloads status', () async {
      final expectedStatus = AppStatus(
        statusText: 'Ready',
        apiBaseUrl: 'https://api.example.com',
        timestamp: DateTime.utc(2026, 5, 14, 10),
        environment: 'test',
      );
      when(() => getAppStatus()).thenAnswer((_) async => expectedStatus);

      final container = ProviderContainer(
        overrides: [
          getAppStatusUseCaseProvider.overrideWithValue(getAppStatus),
        ],
      );
      addTearDown(container.dispose);

      await container.read(appStatusControllerProvider.future);
      await container.read(appStatusControllerProvider.notifier).refresh();

      verify(() => getAppStatus()).called(2);
    });
  });
}
