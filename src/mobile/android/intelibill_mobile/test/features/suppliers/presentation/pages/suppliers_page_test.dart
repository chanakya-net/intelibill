import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/create_supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/pages/suppliers_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/widgets/create_supplier_sheet.dart';
import 'package:mocktail/mocktail.dart';

class _StubSuppliersController extends SuppliersController {
  _StubSuppliersController(this._state);

  final SuppliersState _state;

  @override
  SuppliersState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class MockGetSuppliers extends Mock implements GetSuppliers {}

class MockCreateSupplier extends Mock implements CreateSupplier {}

const _loadedState = SuppliersState(
  suppliers: [
    Supplier(
      supplierId: 'sup-1',
      name: 'Alpha Supplies',
      contactPersonName: 'Alice',
      city: 'Mumbai',
      state: 'Maharashtra',
      isSystem: false,
      isActive: true,
      isPreferred: true,
      balanceDue: 100,
    ),
    Supplier(
      supplierId: 'sup-2',
      name: 'System Supplier',
      contactPersonName: 'Root',
      city: 'Remote',
      isSystem: true,
      isActive: true,
      isPreferred: false,
      balanceDue: 0,
    ),
    Supplier(
      supplierId: 'sup-3',
      name: 'Beta Traders',
      contactPersonName: 'Bob',
      city: 'Pune',
      state: 'Maharashtra',
      isSystem: false,
      isActive: false,
      isPreferred: false,
      balanceDue: 0,
    ),
  ],
);

Widget _buildApp(SuppliersState state) {
  return ProviderScope(
    overrides: [
      suppliersControllerProvider.overrideWith(
        () => _StubSuppliersController(state),
      ),
      authControllerProvider.overrideWith(
        () =>
            _StubAuthController(AuthControllerState(session: _ownerSession())),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const SuppliersPage(),
    ),
  );
}

Widget _buildAppWithOverrides({required SuppliersController controller}) {
  return ProviderScope(
    overrides: [
      suppliersControllerProvider.overrideWith(() => controller),
      authControllerProvider.overrideWith(
        () =>
            _StubAuthController(AuthControllerState(session: _ownerSession())),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const SuppliersPage(),
    ),
  );
}

Widget _buildCreateFlowApp({
  required AuthSession session,
  required MockGetSuppliers getSuppliers,
  required MockCreateSupplier createSupplier,
  Locale? locale,
}) {
  return ProviderScope(
    overrides: [
      getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
      createSupplierUseCaseProvider.overrideWithValue(createSupplier),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      locale: locale,
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const SuppliersPage(),
    ),
  );
}

void main() {
  group('SuppliersPage', () {
    testWidgets('shows loading indicator when isLoading is true', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(const SuppliersState(isLoading: true)));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when supplier list is empty', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(const SuppliersState()));
      await tester.pumpAndSettle();

      expect(find.text('No suppliers found'), findsOneWidget);
    });

    testWidgets('shows error state with retry button when error is set', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(const SuppliersState(failure: NetworkFailure())),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load suppliers'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows supplier cards when suppliers are loaded', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Alpha Supplies'), findsOneWidget);
      expect(find.text('Alice'), findsOneWidget);
      expect(find.text('Beta Traders'), findsOneWidget);
      expect(find.text('Bob'), findsOneWidget);
      expect(find.textContaining('100.00'), findsOneWidget);
    });

    testWidgets('does not show system suppliers in list', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('System Supplier'), findsNothing);
    });

    testWidgets('shows title from localization', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Suppliers'), findsOneWidget);
    });

    testWidgets('shows search field', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('shows RefreshIndicator in loaded state', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });

    testWidgets('pull-to-refresh calls refresh on controller', (tester) async {
      var refreshCount = 0;

      final controller = _CountingRefreshController(_loadedState, () {
        refreshCount++;
      });

      await tester.pumpWidget(_buildAppWithOverrides(controller: controller));
      await tester.pumpAndSettle();

      await tester.fling(find.byType(ListView), const Offset(0, 300), 1000);
      await tester.pumpAndSettle();

      expect(refreshCount, greaterThanOrEqualTo(1));
    });

    testWidgets('filters shown suppliers based on search query', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), 'pune');
      await tester.pump();

      expect(find.text('Beta Traders'), findsOneWidget);
      expect(find.text('Alpha Supplies'), findsNothing);
    });

    testWidgets('shows add action only for owners', (tester) async {
      final getSuppliers = MockGetSuppliers();
      when(getSuppliers.call).thenAnswer((_) async => []);

      await tester.pumpWidget(
        _buildCreateFlowApp(
          session: _ownerSession(role: 'Manager'),
          getSuppliers: getSuppliers,
          createSupplier: MockCreateSupplier(),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(SuppliersPage.addSupplierFabKey), findsNothing);
    });

    testWidgets('creates supplier and shows success snackbar', (tester) async {
      final getSuppliers = MockGetSuppliers();
      final createSupplier = MockCreateSupplier();

      when(getSuppliers.call).thenAnswer((_) async => _loadedState.suppliers);
      when(
        () => createSupplier(
          name: any(named: 'name'),
          contactPersonName: any(named: 'contactPersonName'),
          contactPersonPhone: any(named: 'contactPersonPhone'),
          address: any(named: 'address'),
          city: any(named: 'city'),
          state: any(named: 'state'),
          pin: any(named: 'pin'),
          isActive: any(named: 'isActive'),
          isPreferred: any(named: 'isPreferred'),
        ),
      ).thenAnswer(
        (_) async => const Supplier(
          supplierId: 'sup-4',
          name: 'New Supplier',
          address: '12 Main Street',
          city: 'Mumbai',
          state: 'Maharashtra',
          pin: '400001',
          isSystem: false,
          isActive: true,
          isPreferred: false,
          balanceDue: 0,
        ),
      );

      await tester.pumpWidget(
        _buildCreateFlowApp(
          session: _ownerSession(),
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(SuppliersPage.addSupplierFabKey));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateSupplierSheet.nameFieldKey),
        'New Supplier',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.addressFieldKey),
        '12 Main Street',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.cityFieldKey),
        'Mumbai',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.stateFieldKey),
        'Maharashtra',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.pinFieldKey),
        '400001',
      );

      await tester.ensureVisible(
        find.byKey(CreateSupplierSheet.submitButtonKey),
      );
      await tester.tap(
        find.byKey(CreateSupplierSheet.submitButtonKey),
        warnIfMissed: false,
      );
      await tester.pumpAndSettle();

      expect(find.text('Supplier created successfully.'), findsOneWidget);
      expect(find.byKey(CreateSupplierSheet.nameFieldKey), findsNothing);
      verify(getSuppliers.call).called(greaterThanOrEqualTo(2));
    });

    testWidgets('localizes create success snackbar', (tester) async {
      final getSuppliers = MockGetSuppliers();
      final createSupplier = MockCreateSupplier();

      when(getSuppliers.call).thenAnswer((_) async => _loadedState.suppliers);
      when(
        () => createSupplier(
          name: any(named: 'name'),
          contactPersonName: any(named: 'contactPersonName'),
          contactPersonPhone: any(named: 'contactPersonPhone'),
          address: any(named: 'address'),
          city: any(named: 'city'),
          state: any(named: 'state'),
          pin: any(named: 'pin'),
          isActive: any(named: 'isActive'),
          isPreferred: any(named: 'isPreferred'),
        ),
      ).thenAnswer(
        (_) async => const Supplier(
          supplierId: 'sup-4',
          name: 'New Supplier',
          address: '12 Main Street',
          city: 'Mumbai',
          state: 'Maharashtra',
          pin: '400001',
          isSystem: false,
          isActive: true,
          isPreferred: false,
          balanceDue: 0,
        ),
      );

      await tester.pumpWidget(
        _buildCreateFlowApp(
          session: _ownerSession(),
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
          locale: const Locale('hi'),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(SuppliersPage.addSupplierFabKey));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateSupplierSheet.nameFieldKey),
        'नया आपूर्तिकर्ता',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.addressFieldKey),
        '12 मुख्य सड़क',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.cityFieldKey),
        'मुंबई',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.stateFieldKey),
        'महाराष्ट्र',
      );
      await tester.enterText(
        find.byKey(CreateSupplierSheet.pinFieldKey),
        '400001',
      );

      await tester.ensureVisible(
        find.byKey(CreateSupplierSheet.submitButtonKey),
      );
      await tester.tap(
        find.byKey(CreateSupplierSheet.submitButtonKey),
        warnIfMissed: false,
      );
      await tester.pumpAndSettle();

      expect(find.text('आपूर्तिकर्ता सफलतापूर्वक बनाया गया।'), findsOneWidget);
    });
  });
}

class _CountingRefreshController extends SuppliersController {
  _CountingRefreshController(this._state, this._onRefresh);

  final SuppliersState _state;
  final VoidCallback _onRefresh;

  @override
  SuppliersState build() => _state;

  @override
  Future<void> refresh() async {
    _onRefresh();
  }
}

AuthSession _ownerSession({String role = 'Owner'}) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
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
        lastUsedAt: DateTime.utc(2026, 5, 12, 10),
      ),
    ],
    rememberMe: false,
  );
}
