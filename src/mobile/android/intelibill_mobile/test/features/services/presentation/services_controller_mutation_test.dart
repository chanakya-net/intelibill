import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/activate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/create_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/deactivate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/update_service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetServices extends Mock implements GetServices {}

class MockCreateService extends Mock implements CreateService {}

class MockUpdateService extends Mock implements UpdateService {}

class MockActivateService extends Mock implements ActivateService {}

class MockDeactivateService extends Mock implements DeactivateService {}

final _services = [
  const Service(
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
  const Service(
    serviceId: 'svc-2',
    code: 'SRV-002',
    name: 'Consultation',
    price: 250,
    taxRatePercent: 0,
    taxIncluded: false,
    isActive: false,
  ),
];

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

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getServicesUseCaseProvider.overrideWithValue(getServices),
        createServiceUseCaseProvider.overrideWithValue(createService),
        updateServiceUseCaseProvider.overrideWithValue(updateService),
        activateServiceUseCaseProvider.overrideWithValue(activateService),
        deactivateServiceUseCaseProvider.overrideWithValue(deactivateService),
      ],
    );
  }

  group('ServicesController mutations', () {
    test('creates service and refreshes list on success', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _services);
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
      ).thenAnswer((_) async => _services.first);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final success = await container
          .read(servicesControllerProvider.notifier)
          .createService(
            name: '  Repair  ',
            description: '  Phone repair service  ',
            price: 499.9,
            hsnCode: ' 9987 ',
            taxRatePercent: 18,
            taxIncluded: true,
            isActive: true,
          );

      expect(success, isTrue);
      final state = container.read(servicesControllerProvider);
      expect(state.isSubmitting, isFalse);
      expect(state.submitFailure, isNull);
      verify(
        () => createService(
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
          isActive: any(named: 'isActive'),
        ),
      ).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });

    test('surfaces create failure without clearing it on retry', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _services);
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

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final success = await container
          .read(servicesControllerProvider.notifier)
          .createService(
            name: 'Repair',
            price: 499.9,
            taxRatePercent: 18,
            taxIncluded: true,
            isActive: true,
          );

      expect(success, isFalse);
      final state = container.read(servicesControllerProvider);
      expect(state.isSubmitting, isFalse);
      expect(state.submitFailure, isA<ValidationFailure>());
      verify(
        () => createService(
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
          isActive: any(named: 'isActive'),
        ),
      ).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });

    test('updates service and refreshes list on success', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _services);
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

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final success = await container
          .read(servicesControllerProvider.notifier)
          .updateService(
            serviceId: 'svc-1',
            name: ' Repair Updated ',
            description: ' Updated description ',
            price: 599,
            hsnCode: ' 9988 ',
            taxRatePercent: 12,
            taxIncluded: false,
          );

      expect(success, isTrue);
      expect(container.read(servicesControllerProvider).submitFailure, isNull);
      verify(
        () => updateService(
          serviceId: 'svc-1',
          name: any(named: 'name'),
          description: any(named: 'description'),
          price: any(named: 'price'),
          hsnCode: any(named: 'hsnCode'),
          taxRatePercent: any(named: 'taxRatePercent'),
          taxIncluded: any(named: 'taxIncluded'),
        ),
      ).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });

    test('activates and deactivates services and refreshes list', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _services);
      when(() => activateService('svc-2')).thenAnswer((_) async {});
      when(() => deactivateService('svc-1')).thenAnswer((_) async {});

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final activateSuccess = await container
          .read(servicesControllerProvider.notifier)
          .activateService('svc-2');
      final deactivateSuccess = await container
          .read(servicesControllerProvider.notifier)
          .deactivateService('svc-1');

      expect(activateSuccess, isTrue);
      expect(deactivateSuccess, isTrue);
      verify(() => activateService('svc-2')).called(1);
      verify(() => deactivateService('svc-1')).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(3));
    });
  });
}
