import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/preview_sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/record_sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_return_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockPreviewSaleReturn extends Mock implements PreviewSaleReturn {}

class MockRecordSaleReturn extends Mock implements RecordSaleReturn {}

class _PreviewSaleReturnRequestFake extends Fake
    implements PreviewSaleReturnRequest {}

class _RecordSaleReturnRequestFake extends Fake
    implements RecordSaleReturnRequest {}

class _SaleDetailControllerWithRefreshFailure extends SaleDetailController {
  _SaleDetailControllerWithRefreshFailure(this._state);

  final SaleDetailState _state;

  @override
  SaleDetailState build(String saleId) => _state;

  @override
  Future<void> refresh() async {
    throw AppException(
      failure: Failure.unknown(message: 'Unable to refresh sale detail.'),
    );
  }
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

void main() {
  setUpAll(() {
    registerFallbackValue(_PreviewSaleReturnRequestFake());
    registerFallbackValue(_RecordSaleReturnRequestFake());
  });

  late MockPreviewSaleReturn previewSaleReturn;
  late MockRecordSaleReturn recordSaleReturn;

  setUp(() {
    previewSaleReturn = MockPreviewSaleReturn();
    recordSaleReturn = MockRecordSaleReturn();
  });

  AuthSession _session(String role) => AuthSession(
    accessToken: 'access_token',
    refreshToken: 'refresh_token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: const AuthUser(
      id: 'user-1',
      email: 'test@example.com',
      phoneNumber: null,
      firstName: 'John',
      lastName: 'Doe',
      language: 'en-IN',
    ),
    activeShopId: null,
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Shop',
        role: role,
        isDefault: true,
        lastUsedAt: null,
      ),
    ],
    rememberMe: false,
  );

  SaleDetail _detail() => SaleDetail(
    saleId: 'sale-1',
    invoiceNumber: 'INV-1',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11),
    paidAmount: 0,
    dueAmount: 0,
    totalBeforeDiscount: 0,
    totalDiscountAmount: 0,
    totalAmount: 0,
    totalTaxAmount: 0,
    items: const [
      SaleDetailItem(
        saleItemId: 'goods-1',
        lineType: 'Goods',
        lineCode: 'SKU-1',
        itemName: 'Rice',
        quantity: 5,
        salesPrice: 10,
        originalSalesPrice: 10,
        finalSalesPrice: 10,
        preTaxAmountBeforeDiscount: 50,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 50,
        taxAmount: 9,
        totalAmount: 50,
        savingsAmount: 0,
        taxRatePercent: 18,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 3,
        returnStatus: 'None',
      ),
      SaleDetailItem(
        saleItemId: 'svc-1',
        lineType: 'Service',
        lineCode: 'SV-1',
        itemName: 'Installation',
        quantity: 2,
        salesPrice: 30,
        originalSalesPrice: 30,
        finalSalesPrice: 30,
        preTaxAmountBeforeDiscount: 60,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 60,
        taxAmount: 10.8,
        totalAmount: 60,
        savingsAmount: 0,
        taxRatePercent: 18,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 1,
        returnStatus: 'None',
      ),
    ],
  );

  Future<ProviderContainer> makeContainer({
    required AuthSession? session,
    bool refreshFails = false,
  }) async {
    final detail = SaleDetailState(detail: _detail(), isLoading: false);
    final saleDetailOverride = refreshFails
        ? saleDetailControllerProvider('sale-1').overrideWith(
            () => _SaleDetailControllerWithRefreshFailure(detail),
          )
        : saleDetailControllerProvider('sale-1').overrideWithValue(detail);

    final container = ProviderContainer(
      overrides: [
        saleDetailOverride,
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: session)),
        ),
        previewSaleReturnProvider.overrideWithValue(previewSaleReturn),
        recordSaleReturnProvider.overrideWithValue(recordSaleReturn),
      ],
    );
    await container.read(authControllerProvider.future);
    return container;
  }

  group('SaleReturnController', () {
    test(
      'builds payload for goods and service return lines on preview',
      () async {
        when(
          () => previewSaleReturn(
            saleId: any<String>(named: 'saleId'),
            request: any(named: 'request'),
          ),
        ).thenAnswer(
          (_) async => const SaleReturnPreview(
            saleId: 'sale-1',
            hasFinancialAccess: true,
            lines: [],
            warnings: [],
          ),
        );

        final container = await makeContainer(session: _session('owner'));
        addTearDown(container.dispose);

        final notifier = container.read(
          saleReturnControllerProvider('sale-1').notifier,
        );
        notifier.toggleLine('goods-1', true);
        notifier.updateQuantity('goods-1', 2);
        notifier.updateCondition('goods-1', 1);
        notifier.updateApprovedRefundAmount('goods-1', '90');

        notifier.toggleLine('svc-1', true);
        notifier.updateQuantity('svc-1', 1);
        notifier.updateApprovedRefundAmount('svc-1', '50');

        await notifier.preview();

        final captured = verify(
          () => previewSaleReturn(
            saleId: 'sale-1',
            request: captureAny(named: 'request'),
          ),
        ).captured;

        final request = captured.first as PreviewSaleReturnRequest;
        expect(request.items, hasLength(2));
        expect(_draftFor(request.items, 'goods-1')?.condition, 1);
        expect(_draftFor(request.items, 'svc-1')?.condition, isNull);
      },
    );

    test('sends credit-note payout details on submit', () async {
      final detail = _detail();
      when(
        () => previewSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      ).thenAnswer(
        (_) async => const SaleReturnPreview(
          saleId: 'sale-1',
          hasFinancialAccess: true,
          lines: [],
          warnings: [],
        ),
      );
      when(
        () => recordSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      ).thenAnswer((_) async => detail);

      final container = await makeContainer(session: _session('owner'));
      addTearDown(container.dispose);

      final notifier = container.read(
        saleReturnControllerProvider('sale-1').notifier,
      );
      notifier.toggleLine('goods-1', true);
      notifier.updateQuantity('goods-1', 1);
      notifier.updateCondition('goods-1', 1);
      notifier.updateApprovedRefundAmount('goods-1', '20');
      notifier.updatePayoutDestination(1);
      notifier.updateCreditNoteReason('Damaged');
      notifier.updateCreditNoteExpiresAt(DateTime.utc(2026, 12, 31));
      notifier.updateDueReductionAmount('10');
      notifier.updateDueOverrideReason('Good reason');
      notifier.updateDueOverrideConfirmed(true);

      await notifier.preview();
      await notifier.submit();

      final captured = verify(
        () => recordSaleReturn(
          saleId: 'sale-1',
          request: captureAny(named: 'request'),
        ),
      ).captured;
      final request = captured.first as RecordSaleReturnRequest;
      expect(request.payoutDestination, 1);
      expect(request.creditNoteReason, 'Damaged');
      expect(
        request.creditNoteExpiresAt,
        DateTime.utc(2026, 12, 31).toIso8601String(),
      );
    });

    test('sets payoutMethod for refund destination on submit', () async {
      when(
        () => previewSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      ).thenAnswer(
        (_) async => const SaleReturnPreview(
          saleId: 'sale-1',
          hasFinancialAccess: true,
          lines: [],
          warnings: [],
        ),
      );
      when(
        () => recordSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      ).thenAnswer((_) async => _detail());

      final container = await makeContainer(session: _session('owner'));
      addTearDown(container.dispose);

      final notifier = container.read(
        saleReturnControllerProvider('sale-1').notifier,
      );
      notifier.toggleLine('goods-1', true);
      notifier.updateQuantity('goods-1', 1);
      notifier.updateCondition('goods-1', 1);
      notifier.updateApprovedRefundAmount('goods-1', '20');
      notifier.updatePayoutDestination(2);

      await notifier.preview();
      await notifier.submit();

      final captured = verify(
        () => recordSaleReturn(
          saleId: 'sale-1',
          request: captureAny(named: 'request'),
        ),
      ).captured;
      final request = captured.first as RecordSaleReturnRequest;
      expect(request.payoutDestination, 2);
      expect(request.payoutMethod, 2);
    });

    test(
      'does not fail submit if sale detail refresh fails after recording',
      () async {
        when(
          () => recordSaleReturn(
            saleId: any<String>(named: 'saleId'),
            request: any(named: 'request'),
          ),
        ).thenAnswer((_) async => _detail());

        final container = await makeContainer(
          session: _session('owner'),
          refreshFails: true,
        );
        addTearDown(container.dispose);

        final notifier = container.read(
          saleReturnControllerProvider('sale-1').notifier,
        );
        notifier.toggleLine('goods-1', true);
        notifier.updateQuantity('goods-1', 1);
        notifier.updateCondition('goods-1', 1);
        notifier.updateApprovedRefundAmount('goods-1', '20');
        notifier.updatePayoutDestination(2);

        await notifier.submit();

        final state = container.read(saleReturnControllerProvider('sale-1'));
        expect(state.failure, isNull);
        expect(state.isSubmitting, isFalse);
      },
    );

    test('fails validation when credit note expiry is missing', () async {
      final container = await makeContainer(session: _session('owner'));
      addTearDown(container.dispose);

      final notifier = container.read(
        saleReturnControllerProvider('sale-1').notifier,
      );
      notifier.toggleLine('goods-1', true);
      notifier.updateQuantity('goods-1', 1);
      notifier.updateCondition('goods-1', 1);
      notifier.updateApprovedRefundAmount('goods-1', '20');
      notifier.updatePayoutDestination(1);
      notifier.updateCreditNoteReason('Damaged');

      await notifier.submit();

      final state = container.read(saleReturnControllerProvider('sale-1'));
      expect(state.failure, isA<ValidationFailure>());
      final failure = state.failure;
      expect(failure, isA<ValidationFailure>());
      if (failure is ValidationFailure) {
        expect(failure.message, isNotNull);
      }
      expect(state.isSubmitting, isFalse);
    });

    test('keeps failure when submit throws', () async {
      when(
        () => recordSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.validation(message: 'Bad input')),
      );

      final container = await makeContainer(session: _session('owner'));
      addTearDown(container.dispose);

      final notifier = container.read(
        saleReturnControllerProvider('sale-1').notifier,
      );
      notifier.toggleLine('goods-1', true);
      notifier.updateQuantity('goods-1', 1);
      notifier.updateCondition('goods-1', 1);
      notifier.updateApprovedRefundAmount('goods-1', '20');
      notifier.updatePayoutDestination(2);

      await notifier.submit();

      final state = container.read(saleReturnControllerProvider('sale-1'));
      expect(state.failure, isA<ValidationFailure>());
      expect(state.isSubmitting, isFalse);
    });

    test(
      'sets failure when preview call throws and keeps state failure',
      () async {
        when(
          () => previewSaleReturn(
            saleId: any<String>(named: 'saleId'),
            request: any(named: 'request'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.validation(message: 'Bad input')),
        );

        final container = await makeContainer(session: _session('owner'));
        addTearDown(container.dispose);

        final notifier = container.read(
          saleReturnControllerProvider('sale-1').notifier,
        );
        notifier.toggleLine('goods-1', true);
        notifier.updateQuantity('goods-1', 1);
        notifier.updateApprovedRefundAmount('goods-1', '20');

        await notifier.preview();

        final state = container.read(saleReturnControllerProvider('sale-1'));
        expect(state.failure, isNotNull);
        expect(state.failure, isA<ValidationFailure>());
        expect(state.isPreviewLoading, isFalse);
      },
    );

    test('blocks submit for staff role', () async {
      final container = await makeContainer(session: _session('staff'));
      addTearDown(container.dispose);

      final notifier = container.read(
        saleReturnControllerProvider('sale-1').notifier,
      );
      notifier.toggleLine('goods-1', true);
      notifier.updateQuantity('goods-1', 1);
      notifier.updateApprovedRefundAmount('goods-1', '10');

      await notifier.submit();

      final state = container.read(saleReturnControllerProvider('sale-1'));
      expect(state.canSubmitReturns, isFalse);
      expect(state.failure, isA<ForbiddenFailure>());
      verifyNever(
        () => recordSaleReturn(
          saleId: any<String>(named: 'saleId'),
          request: any(named: 'request'),
        ),
      );
    });
  });
}

SaleReturnLineDraft? _draftFor(
  List<SaleReturnLineDraft> items,
  String saleItemId,
) {
  for (final item in items) {
    if (item.saleItemId == saleItemId) {
      return item;
    }
  }
  return null;
}
