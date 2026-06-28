import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/activate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/create_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/deactivate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/update_service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:intelibill_mobile/src/features/services/presentation/widgets/edit_service_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetServices extends Mock implements GetServices {}

class MockCreateService extends Mock implements CreateService {}

class MockUpdateService extends Mock implements UpdateService {}

class MockActivateService extends Mock implements ActivateService {}

class MockDeactivateService extends Mock implements DeactivateService {}

const _service = Service(
  serviceId: 'svc-1',
  code: 'SRV-001',
  name: 'Repair',
  description: 'Phone repair service',
  price: 499.9,
  hsnCode: '9987',
  taxRatePercent: 18,
  taxIncluded: true,
  isActive: true,
);

Widget _buildApp({
  required MockGetServices getServices,
  required MockCreateService createService,
  required MockUpdateService updateService,
  required MockActivateService activateService,
  required MockDeactivateService deactivateService,
}) {
  return ProviderScope(
    overrides: [
      getServicesUseCaseProvider.overrideWithValue(getServices),
      createServiceUseCaseProvider.overrideWithValue(createService),
      updateServiceUseCaseProvider.overrideWithValue(updateService),
      activateServiceUseCaseProvider.overrideWithValue(activateService),
      deactivateServiceUseCaseProvider.overrideWithValue(deactivateService),
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
                      builder: (context) =>
                          const EditServiceSheet(service: _service),
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

void main() {
  late MockGetServices getServices;
  late MockCreateService createService;
  late MockUpdateService updateService;
  late MockActivateService activateService;
  late MockDeactivateService deactivateService;

  setUp(() {
    getServices = MockGetServices();
    createService = MockCreateService();
    updateService = MockUpdateService();
    activateService = MockActivateService();
    deactivateService = MockDeactivateService();
  });

  group('EditServiceSheet', () {
    testWidgets('prefills fields from the service', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);
      when(
        () => updateService(
          serviceId: any(named: 'serviceId'),
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
        ),
      ).thenAnswer((_) async {});

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      expect(find.text('Repair'), findsWidgets);
      expect(find.text('Phone repair service'), findsWidgets);
      expect(find.text('499.90'), findsWidgets);
      expect(find.text('9987'), findsWidgets);
      expect(find.text('18.00'), findsWidgets);
    });

    testWidgets('keeps sheet open and shows failure message', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);
      when(
        () => updateService(
          serviceId: any(named: 'serviceId'),
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
        ),
      ).thenThrow(
        AppException(
          failure: const Failure.validation(message: 'Invalid update'),
        ),
      );

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(EditServiceSheet.nameFieldKey),
        'Repair Updated',
      );
      await tester.tap(find.byKey(EditServiceSheet.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Invalid update'), findsOneWidget);
      expect(find.byKey(EditServiceSheet.submitButtonKey), findsOneWidget);
    });

    testWidgets('submits updated service and closes sheet', (tester) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => const []);
      when(
        () => updateService(
          serviceId: any(named: 'serviceId'),
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
        ),
      ).thenAnswer((_) async {});

      await tester.pumpWidget(
        _buildApp(
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(EditServiceSheet.nameFieldKey),
        'Repair Updated',
      );
      await tester.enterText(
        find.byKey(EditServiceSheet.descriptionFieldKey),
        'Updated description',
      );
      await tester.tap(find.byKey(EditServiceSheet.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.byKey(EditServiceSheet.nameFieldKey), findsNothing);
      verify(
        () => updateService(
          serviceId: 'svc-1',
          name: 'Repair Updated',
          description: 'Updated description',
          price: 499.9,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: true,
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
