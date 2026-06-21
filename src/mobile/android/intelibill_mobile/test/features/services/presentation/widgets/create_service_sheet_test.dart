import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/create_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/update_service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:intelibill_mobile/src/features/services/presentation/widgets/create_service_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetServices extends Mock implements GetServices {}

class MockCreateService extends Mock implements CreateService {}

class MockUpdateService extends Mock implements UpdateService {}

Widget _buildApp({
  required MockGetServices getServices,
  required MockCreateService createService,
  required MockUpdateService updateService,
}) {
  return ProviderScope(
    overrides: [
      getServicesUseCaseProvider.overrideWithValue(getServices),
      createServiceUseCaseProvider.overrideWithValue(createService),
      updateServiceUseCaseProvider.overrideWithValue(updateService),
    ],
    child: MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Builder(
        builder: (context) {
          return Scaffold(
            body: Center(
              child: ElevatedButton(
                onPressed: () {
                  unawaited(
                    showModalBottomSheet<bool>(
                      context: context,
                      isScrollControlled: true,
                      useSafeArea: true,
                      builder: (context) => const CreateServiceSheet(),
                    ),
                  );
                },
                child: const Text('Open'),
              ),
            ),
          );
        },
      ),
    ),
  );
}

Future<void> _tapSubmit(WidgetTester tester) async {
  final submitButton = find.byKey(CreateServiceSheet.submitButtonKey);
  await tester.ensureVisible(submitButton);
  await tester.pumpAndSettle();
  await tester.tap(submitButton);
  await tester.pumpAndSettle();
}

void main() {
  late MockGetServices getServices;
  late MockCreateService createService;
  late MockUpdateService updateService;

  setUp(() {
    getServices = MockGetServices();
    createService = MockCreateService();
    updateService = MockUpdateService();
  });

  group('CreateServiceSheet', () {
    testWidgets('validates required fields and numeric ranges', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateServiceSheet.hsnCodeFieldKey),
        '12',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.taxRateFieldKey),
        '120',
      );
      await _tapSubmit(tester);

      expect(find.text('Service name is required.'), findsOneWidget);
      expect(find.text('Price is required.'), findsOneWidget);
      expect(find.text('HSN code must be 4 to 8 digits.'), findsOneWidget);
      expect(
        find.text('Tax rate must be between 0 and 100.'),
        findsOneWidget,
      );
    });

    testWidgets('keeps sheet open and shows failure message', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);
      when(
        () => createService(
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
          isActive: any(named: 'isActive'),
        ),
      ).thenThrow(
        AppException(
          failure: const Failure.validation(message: 'Invalid service'),
        ),
      );

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateServiceSheet.nameFieldKey),
        'Repair',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.priceFieldKey),
        '499.90',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.descriptionFieldKey),
        'Phone repair service',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.hsnCodeFieldKey),
        '9987',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.taxRateFieldKey),
        '18',
      );
      await _tapSubmit(tester);

      expect(find.text('Invalid service'), findsOneWidget);
      expect(find.byKey(CreateServiceSheet.submitButtonKey), findsOneWidget);
    });

    testWidgets('submits valid form and closes sheet', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);
      when(
        () => createService(
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer(
        (_) async => const Service(
          serviceId: 'svc-1',
          code: 'SRV-001',
          name: 'Repair',
          description: 'Phone repair service',
          price: 499.9,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: true,
          isActive: true,
        ),
      );

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateServiceSheet.nameFieldKey),
        'Repair',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.descriptionFieldKey),
        'Phone repair service',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.priceFieldKey),
        '499.90',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.hsnCodeFieldKey),
        '9987',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.taxRateFieldKey),
        '18',
      );
      await _tapSubmit(tester);

      expect(find.byKey(CreateServiceSheet.nameFieldKey), findsNothing);
      verify(
        () => createService(
          name: 'Repair',
          description: 'Phone repair service',
          price: 499.9,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: true,
          isActive: true,
        ),
      ).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });
  });
}
