import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/activate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/create_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/deactivate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/update_service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:intelibill_mobile/src/features/services/presentation/pages/services_page.dart';
import 'package:intelibill_mobile/src/features/services/presentation/widgets/create_service_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetServices extends Mock implements GetServices {}

class MockCreateService extends Mock implements CreateService {}

class MockUpdateService extends Mock implements UpdateService {}

class MockActivateService extends Mock implements ActivateService {}

class MockDeactivateService extends Mock implements DeactivateService {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

AuthSession _session(String role) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 6, 1),
    refreshTokenExpiresAt: DateTime.utc(2026, 7, 1),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Primary Shop',
        role: role,
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 6, 1),
      ),
    ],
    rememberMe: false,
  );
}

const _services = [
  Service(
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
  Service(
    serviceId: 'svc-2',
    code: 'SRV-002',
    name: 'Consultation',
    description: null,
    price: 250,
    hsnCode: null,
    taxRatePercent: 0,
    taxIncluded: false,
    isActive: false,
  ),
];

Widget _buildApp({
  required AuthSession session,
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
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const ServicesPage(),
    ),
  );
}

Future<void> _tapCreateSubmit(WidgetTester tester) async {
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
  late MockActivateService activateService;
  late MockDeactivateService deactivateService;

  setUp(() {
    getServices = MockGetServices();
    createService = MockCreateService();
    updateService = MockUpdateService();
    activateService = MockActivateService();
    deactivateService = MockDeactivateService();
  });

  group('ServicesPage', () {
    testWidgets('shows management actions for owner and hides them for staff', (
      tester,
    ) async {
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((_) async => _services);

      await tester.pumpWidget(
        _buildApp(
          session: _session('Owner'),
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(ServicesPage.addServiceFabKey), findsOneWidget);
      expect(
        find.byKey(ServicesPage.editServiceActionKey('svc-1')),
        findsOneWidget,
      );
      expect(
        find.byKey(ServicesPage.toggleServiceActionKey('svc-1')),
        findsOneWidget,
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();

      await tester.pumpWidget(
        _buildApp(
          session: _session('Staff'),
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(ServicesPage.addServiceFabKey), findsNothing);
      expect(
        find.byKey(ServicesPage.editServiceActionKey('svc-1')),
        findsNothing,
      );
      expect(
        find.byKey(ServicesPage.toggleServiceActionKey('svc-1')),
        findsNothing,
      );
    });

    testWidgets('owner can add a service from the page and sees success', (
      tester,
    ) async {
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
      ).thenAnswer(
        (_) async => const Service(
          serviceId: 'svc-3',
          code: 'SRV-003',
          name: 'Installation',
          description: 'Hardware setup',
          price: 799.9,
          hsnCode: '9988',
          taxRatePercent: 18,
          taxIncluded: true,
          isActive: true,
        ),
      );

      await tester.pumpWidget(
        _buildApp(
          session: _session('Owner'),
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(ServicesPage.addServiceFabKey));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateServiceSheet.nameFieldKey),
        'Installation',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.descriptionFieldKey),
        'Hardware setup',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.priceFieldKey),
        '799.90',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.hsnCodeFieldKey),
        '9988',
      );
      await tester.enterText(
        find.byKey(CreateServiceSheet.taxRateFieldKey),
        '18',
      );
      await _tapCreateSubmit(tester);

      expect(find.text('Service created successfully.'), findsOneWidget);
      expect(find.byKey(CreateServiceSheet.nameFieldKey), findsNothing);
      verify(
        () => createService(
          name: 'Installation',
          description: 'Hardware setup',
          price: 799.9,
          hsnCode: '9988',
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

    testWidgets('owner can toggle service active state and refreshes list', (
      tester,
    ) async {
      var loadCount = 0;
      when(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).thenAnswer((invocation) async {
        loadCount += 1;
        return loadCount == 1
            ? [_services.first]
            : [
                const Service(
                  serviceId: 'svc-1',
                  code: 'SRV-001',
                  name: 'Repair',
                  description: 'Phone repair service',
                  price: 499.9,
                  hsnCode: '9987',
                  taxRatePercent: 18,
                  taxIncluded: true,
                  isActive: false,
                ),
              ];
      });
      when(() => deactivateService('svc-1')).thenAnswer((_) async {});

      await tester.pumpWidget(
        _buildApp(
          session: _session('Owner'),
          getServices: getServices,
          createService: createService,
          updateService: updateService,
          activateService: activateService,
          deactivateService: deactivateService,
        ),
      );
      await tester.pumpAndSettle();

      expect(
        find.descendant(
          of: find.byKey(ServicesPage.statusServiceChipKey('svc-1')),
          matching: find.text('Active'),
        ),
        findsOneWidget,
      );
      await tester.tap(
        find.byKey(ServicesPage.toggleServiceActionKey('svc-1')),
      );
      await tester.pumpAndSettle();

      expect(find.text('Service deactivated successfully.'), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(ServicesPage.statusServiceChipKey('svc-1')),
          matching: find.text('Inactive'),
        ),
        findsOneWidget,
      );
      verify(() => deactivateService('svc-1')).called(1);
      verify(
        () => getServices(
          includeInactive: any(named: 'includeInactive'),
          search: any(named: 'search'),
        ),
      ).called(greaterThanOrEqualTo(2));
    });
  });
}
