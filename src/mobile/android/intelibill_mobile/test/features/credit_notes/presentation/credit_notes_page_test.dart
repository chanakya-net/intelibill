import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class _StubCreditNotesController extends CreditNotesController {
  _StubCreditNotesController(this._state);

  final CreditNotesState _state;

  @override
  CreditNotesState build() => _state;

  @override
  Future<void> openByCode(String code) async {
    final index = state.notes.indexWhere((note) => note.code == code);
    if (index != -1) {
      state = state.copyWith(selectedNote: state.notes[index]);
    }
  }
}

AuthSession _ownerSession() {
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
      lastName: 'Sharma',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Primary Shop',
        role: 'Owner',
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 5, 12, 10),
      ),
    ],
    rememberMe: false,
  );
}

void main() {
  testWidgets('opens receipt from credit note detail sheet', (tester) async {
    final state = CreditNotesState(
      isLoading: false,
      notes: [
        const CreditNote(
          creditNoteId: 'cn-1',
          code: 'CN-001',
          status: 'active',
          originalAmount: 1000,
          availableBalance: 1000,
          expiresAt: null,
          isVoided: false,
          saleReturnId: 'ret-1',
          reason: 'Damaged',
          voidReason: null,
          returnNumber: 'RET-001',
          invoiceNumber: 'INV-001',
          customerName: 'John',
        ),
      ],
    );

    final container = ProviderContainer(
      overrides: [
        creditNotesControllerProvider.overrideWith(
          () => _StubCreditNotesController(state),
        ),
        authControllerProvider.overrideWith(
          () => _StubAuthController(
            AuthControllerState(session: _ownerSession()),
          ),
        ),
        creditNotePrintByCodeProvider.overrideWith(
          (ref, _) => Future.value(
            CreditNotePrint(
              creditNoteId: 'cn-1',
              code: 'CN-001',
              status: 'active',
              isUsable: true,
              originalAmount: 1000,
              availableBalance: 1000,
              issuedAt: DateTime.utc(2026, 6, 10, 10),
              expiresAt: DateTime.utc(2026, 7, 1, 10),
              saleId: 'sale-1',
              invoiceNumber: 'INV-001',
              saleReturnId: 'ret-1',
              returnNumber: 'RET-001',
              customerDisplayName: 'John',
              reason: 'Damaged',
              voidReason: null,
            ),
          ),
        ),
      ],
    );
    addTearDown(container.dispose);

    final router = container.read(goRouterProvider);
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(
          locale: const Locale('en', 'IN'),
          supportedLocales: const [Locale('en', 'IN')],
          localizationsDelegates: const [
            AppLocalizations.delegate,
            ...AppLocalizations.localizationsDelegates,
          ],
          routerConfig: router,
        ),
      ),
    );
    await tester.pump();
    for (var i = 0; i < 4; i++) {
      await tester.pump(const Duration(milliseconds: 16));
    }

    router.go(AppRoutes.creditNotes);
    await tester.pump(const Duration(milliseconds: 10));
    await tester.pump(const Duration(milliseconds: 300));

    container
        .read(creditNotesControllerProvider.notifier)
        .selectNote(state.notes.first);
    await tester.pump(const Duration(milliseconds: 10));
    await tester.pump(const Duration(milliseconds: 300));

    expect(
      find.byKey(const Key('credit-note-open-receipt-button')),
      findsOneWidget,
    );
    final openReceiptButton = tester.widget<TextButton>(
      find.byKey(const Key('credit-note-open-receipt-button')),
    );
    expect(openReceiptButton.onPressed, isNotNull);
    openReceiptButton.onPressed!.call();
    await tester.pump(const Duration(milliseconds: 600));
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 16));
    }

    expect(container.read(creditNotesControllerProvider).selectedNote, isNull);
    expect(find.text('Credit note receipt'), findsOneWidget);
  });
}
