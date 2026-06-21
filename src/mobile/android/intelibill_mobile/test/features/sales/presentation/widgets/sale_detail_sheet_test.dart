import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/void_sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_detail_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

class MockVoidSaleReturn extends Mock implements VoidSaleReturn {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

AuthSession _sessionForRole(String role) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: AuthUser(
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
        role: role,
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 5, 12, 10),
      ),
    ],
    rememberMe: false,
  );
}

SaleDetail _saleDetail({bool isReturnVoided = false}) {
  return SaleDetail(
    saleId: 'sale-1',
    invoiceNumber: 'INV-2026-001',
    customerId: null,
    customerName: 'John Doe',
    customerPhone: '9999999999',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11, 10, 30),
    items: [
      SaleDetailItem(
        saleItemId: 'item-1',
        lineType: 'Goods',
        lineCode: 'SKU-1',
        itemName: 'Notebook',
        quantity: 2,
        salesPrice: 100,
        originalSalesPrice: 100,
        finalSalesPrice: 100,
        preTaxAmountBeforeDiscount: 200,
        itemDiscountAmount: 0,
        saleDiscountAmount: 20,
        taxableAmount: 218,
        taxAmount: 18,
        totalAmount: 236,
        savingsAmount: 20,
        taxRatePercent: 18,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 1,
        returnableQuantity: 1,
        returnStatus: 'PartiallyReturned',
      ),
    ],
    settlements: [
      SaleDetailSettlement(
        settlementId: 'settlement-1',
        method: 'Cash',
        amount: 200,
        settledAt: DateTime.utc(2026, 5, 11, 11),
      ),
    ],
    discounts: [
      SaleDetailDiscount(
        discountId: 'discount-1',
        type: 'Promo',
        value: '10%',
        amount: 20,
      ),
    ],
    returns: [
      SaleDetailReturn(
        saleReturnId: 'return-1',
        returnNumber: 'RET-001',
        processedAt: DateTime.utc(2026, 5, 12, 9),
        processedBy: 'Manager',
        totalRefundAmount: 100,
        dueReductionAmount: 0,
        payoutAmount: 100,
        totalTaxableAmount: 100,
        totalTaxAmount: 0,
        isVoided: isReturnVoided,
        voidedAt: isReturnVoided ? DateTime.utc(2026, 5, 13, 10, 15) : null,
        voidReason: isReturnVoided ? 'Duplicate return' : null,
        items: [
          SaleDetailReturnItem(
            saleReturnItemId: 'return-item-1',
            saleItemId: 'item-1',
            quantity: 1,
            approvedRefundAmount: 100,
            taxableAmount: 100,
            taxAmount: 0,
          ),
        ],
      ),
    ],
    creditNoteRedemptions: [
      SaleDetailCreditNoteRedemption(
        creditNoteId: 'redemption-1',
        code: 'CN-LOYALTY-001',
        appliedAmount: 15,
      ),
    ],
    warnings: const ['Low stock detected'],
    paidAmount: 200,
    dueAmount: 36,
    totalBeforeDiscount: 256,
    totalDiscountAmount: 20,
    totalAmount: 236,
    totalTaxAmount: 18,
    creditNoteAppliedAmount: 15,
    status: 'partiallyPaid',
    refundAmount: 0.0,
    dueReductionAmount: 0.0,
  );
}

Widget _buildApp({
  required MockGetSaleDetail getSaleDetail,
  required MockVoidSaleReturn voidSaleReturn,
  required String role,
  required SaleDetail detail,
  MediaQueryData? mediaQueryData,
}) {
  return ProviderScope(
    overrides: [
      getSaleDetailUseCaseProvider.overrideWithValue(getSaleDetail),
      voidSaleReturnUseCaseProvider.overrideWithValue(voidSaleReturn),
      authControllerProvider.overrideWith(
        () => _StubAuthController(
          AuthControllerState(session: _sessionForRole(role)),
        ),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      locale: const Locale('en', 'IN'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      builder: (context, child) {
        final data = mediaQueryData ?? MediaQuery.maybeOf(context);
        if (data == null || child == null) {
          return child ?? const SizedBox.shrink();
        }

        return MediaQuery(data: data, child: child);
      },
      home: Scaffold(
        body: SaleDetailSheet(saleId: detail.saleId),
      ),
    ),
  );
}

String _voidReturnActionKey(String action, String id) {
  return 'sales-detail-return-$action-$id';
}

void main() {
  late MockGetSaleDetail getSaleDetail;
  late MockVoidSaleReturn voidSaleReturn;

  setUp(() {
    getSaleDetail = MockGetSaleDetail();
    voidSaleReturn = MockVoidSaleReturn();
  });

  testWidgets('shows void action for non-voided return for owner', (
    tester,
  ) async {
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Owner',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(Key(_voidReturnActionKey('button', 'return-1'))),
      findsOneWidget,
    );
  });

  testWidgets('hides void action for staff users', (tester) async {
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Staff',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(Key(_voidReturnActionKey('button', 'return-1'))),
      findsNothing,
    );
  });

  testWidgets('does not show void action for already voided returns', (
    tester,
  ) async {
    final detail = _saleDetail(isReturnVoided: true);
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Manager',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(Key(_voidReturnActionKey('button', 'return-1'))),
      findsNothing,
    );
    expect(find.text('Voided'), findsOneWidget);
  });

  testWidgets('requires reason before voiding return', (tester) async {
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Owner',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();
    final actionButton = find.byKey(
      Key(_voidReturnActionKey('button', 'return-1')),
    );
    await tester.ensureVisible(actionButton);
    await tester.pumpAndSettle();
    await tester.tap(
      actionButton,
    );
    await tester.pumpAndSettle();
    final submitButton = find.byKey(
      Key(_voidReturnActionKey('void-submit', 'return-1')),
    );
    await tester.ensureVisible(submitButton);
    await tester.pumpAndSettle();

    await tester.tap(
      submitButton,
    );
    await tester.pumpAndSettle();

    verifyNever(() => voidSaleReturn(saleReturnId: 'return-1', reason: ''));
    expect(find.text('Reason is required.'), findsOneWidget);
  });

  testWidgets('shows backend conflict message on void failure', (tester) async {
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);
    when(
      () => voidSaleReturn(
        saleReturnId: 'return-1',
        reason: 'Redeemed',
      ),
    ).thenThrow(
      AppException(
        failure: const Failure.server(message: 'Credit note already redeemed'),
      ),
    );

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Manager',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();
    final actionButton = find.byKey(
      Key(_voidReturnActionKey('button', 'return-1')),
    );
    await tester.ensureVisible(actionButton);
    await tester.pumpAndSettle();

    await tester.tap(
      actionButton,
    );
    await tester.pumpAndSettle();
    final reasonField = find.byKey(
      Key(_voidReturnActionKey('void-reason', 'return-1')),
    );
    final submitButton = find.byKey(
      Key(_voidReturnActionKey('void-submit', 'return-1')),
    );
    await tester.ensureVisible(reasonField);
    await tester.pumpAndSettle();

    await tester.enterText(reasonField, 'Redeemed');
    await tester.ensureVisible(submitButton);
    await tester.pumpAndSettle();
    await tester.tap(submitButton);
    await tester.pumpAndSettle();

    expect(find.text('Credit note already redeemed'), findsOneWidget);
    verify(
      () => voidSaleReturn(saleReturnId: 'return-1', reason: 'Redeemed'),
    ).called(1);
  });

  testWidgets('shows void-specific forbidden fallback message', (tester) async {
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);
    when(
      () => voidSaleReturn(
        saleReturnId: 'return-1',
        reason: 'Forbidden',
      ),
    ).thenThrow(
      AppException(failure: const Failure.forbidden()),
    );

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Manager',
        detail: detail,
      ),
    );
    await tester.pumpAndSettle();

    final actionButton = find.byKey(
      Key(_voidReturnActionKey('button', 'return-1')),
    );
    await tester.ensureVisible(actionButton);
    await tester.pumpAndSettle();
    await tester.tap(
      actionButton,
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(Key(_voidReturnActionKey('void-reason', 'return-1'))),
      'Forbidden',
    );
    await tester.tap(
      find.byKey(Key(_voidReturnActionKey('void-submit', 'return-1'))),
    );
    await tester.pumpAndSettle();

    expect(
      find.text('You do not have permission to void returns.'),
      findsOneWidget,
    );
    expect(
      find.text('You do not have permission to view sales history.'),
      findsNothing,
    );
  });

  testWidgets('wraps void-return sheet for keyboard-safe scrolling', (
    tester,
  ) async {
    const keyboardInsets = EdgeInsets.only(bottom: 240);
    final detail = _saleDetail();
    when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

    await tester.pumpWidget(
      _buildApp(
        getSaleDetail: getSaleDetail,
        voidSaleReturn: voidSaleReturn,
        role: 'Owner',
        detail: detail,
        mediaQueryData: const MediaQueryData(
          size: Size(320, 480),
          viewInsets: keyboardInsets,
        ),
      ),
    );
    await tester.pumpAndSettle();

    final actionButton = find.byKey(
      Key(_voidReturnActionKey('button', 'return-1')),
    );
    await tester.ensureVisible(actionButton);
    await tester.pumpAndSettle();
    await tester.tap(
      actionButton,
    );
    await tester.pumpAndSettle();

    expect(
      find.descendant(
        of: find.byType(BottomSheet),
        matching: find.byType(SafeArea),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byType(BottomSheet),
        matching: find.byType(SingleChildScrollView),
      ),
      findsOneWidget,
    );

    final paddings = tester.widgetList<Padding>(
      find.descendant(
        of: find.byType(BottomSheet),
        matching: find.byType(Padding),
      ),
    );
    final hasInsetAwarePadding = paddings.any((widget) {
      final padding = widget.padding;
      return padding is EdgeInsets &&
          padding.left == 16 &&
          padding.top == 0 &&
          padding.right == 16 &&
          padding.bottom == 24 + keyboardInsets.bottom;
    });

    expect(hasInsetAwarePadding, isTrue);
  });
}
