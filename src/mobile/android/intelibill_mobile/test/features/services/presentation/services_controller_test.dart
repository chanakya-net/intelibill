import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetServices extends Mock implements GetServices {}

final _testServices = [
  const Service(
    serviceId: 'svc-1',
    code: 'SVC-001',
    name: 'Repair',
    price: 100,
    taxRatePercent: 18,
    taxIncluded: true,
    isActive: true,
  ),
  const Service(
    serviceId: 'svc-2',
    code: 'SVC-002',
    name: 'Maintenance',
    description: 'Monthly checkup',
    price: 200,
    taxRatePercent: 12,
    taxIncluded: false,
    isActive: false,
  ),
  const Service(
    serviceId: 'svc-3',
    code: 'SVC-003',
    name: 'Installation',
    description: 'Setup hardware',
    price: 500,
    taxRatePercent: 18,
    taxIncluded: true,
    isActive: true,
  ),
];

void main() {
  late MockGetServices getServices;

  setUp(() {
    getServices = MockGetServices();
  });

  ProviderContainer makeContainer({required MockGetServices getServices}) {
    return ProviderContainer(
      overrides: [getServicesUseCaseProvider.overrideWithValue(getServices)],
    );
  }

  group('ServicesController', () {
    test('starts in loading state', () {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _testServices);

      final container = makeContainer(getServices: getServices);
      addTearDown(container.dispose);

      expect(container.read(servicesControllerProvider).isLoading, isTrue);
      expect(container.read(servicesControllerProvider).services, isEmpty);
    });

    test('loads services and transitions to loaded state', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _testServices);

      final container = makeContainer(getServices: getServices);
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final state = container.read(servicesControllerProvider);
      expect(state.isLoading, isFalse);
      expect(state.services, _testServices);
      expect(state.failure, isNull);
      expect(state.filteredServices.length, 3);
    });

    test('handles refresh failure', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenThrow(AppException(failure: const Failure.unknown()));

      final container = makeContainer(getServices: getServices);
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();

      final state = container.read(servicesControllerProvider);
      expect(state.isLoading, isFalse);
      expect(state.services, isEmpty);
      expect(state.failure, isA<UnknownFailure>());
    });

    test('searches services by name and code', () async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _testServices);

      final container = makeContainer(getServices: getServices);
      addTearDown(container.dispose);

      await container.read(servicesControllerProvider.notifier).refresh();
      container
          .read(servicesControllerProvider.notifier)
          .updateSearch('repair');

      final filtered = container
          .read(servicesControllerProvider)
          .filteredServices;
      expect(filtered.length, 1);
      expect(filtered.first.code, 'SVC-001');
    });

    group('filter by status', () {
      test('shows only active services when active filter selected', () async {
        when(
          () => getServices(
            includeInactive: false,
            search: any(named: 'search'),
          ),
        ).thenAnswer(
          (_) async =>
              _testServices.where((service) => service.isActive).toList(),
        );

        final container = makeContainer(getServices: getServices);
        addTearDown(container.dispose);

        container
            .read(servicesControllerProvider.notifier)
            .setFilter(
              ServiceFilterOption.active,
            );
        await container.read(servicesControllerProvider.notifier).refresh();

        final filtered = container
            .read(servicesControllerProvider)
            .filteredServices;
        expect(filtered.every((service) => service.isActive), isTrue);
        expect(filtered.length, 2);
      });

      test(
        'shows only inactive services when inactive filter selected',
        () async {
          when(
            () => getServices(
              includeInactive: true,
              search: any(named: 'search'),
            ),
          ).thenAnswer((_) async => _testServices);

          final container = makeContainer(getServices: getServices);
          addTearDown(container.dispose);

          container
              .read(servicesControllerProvider.notifier)
              .setFilter(
                ServiceFilterOption.inactive,
              );
          await container.read(servicesControllerProvider.notifier).refresh();

          final filtered = container
              .read(servicesControllerProvider)
              .filteredServices;
          expect(filtered.length, 1);
          expect(filtered.first.code, 'SVC-002');
        },
      );

      test('shows all services when all filter selected', () async {
        when(
          () => getServices(
            includeInactive: any(named: 'includeInactive'),
            search: any(named: 'search'),
          ),
        ).thenAnswer((_) async => _testServices);

        final container = makeContainer(getServices: getServices);
        addTearDown(container.dispose);

        container
            .read(servicesControllerProvider.notifier)
            .setFilter(
              ServiceFilterOption.active,
            );
        await container.read(servicesControllerProvider.notifier).refresh();
        container
            .read(servicesControllerProvider.notifier)
            .setFilter(
              ServiceFilterOption.all,
            );

        final filtered = container
            .read(servicesControllerProvider)
            .filteredServices;
        expect(filtered.length, 3);
      });
    });
  });
}
