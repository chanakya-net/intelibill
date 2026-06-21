import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/disable_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/replace_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discount_editor_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockPreviewDiscount extends Mock implements PreviewDiscount {}

class MockCreateDiscount extends Mock implements CreateDiscount {}

class MockReplaceDiscount extends Mock implements ReplaceDiscount {}

class MockDisableDiscount extends Mock implements DisableDiscount {}

class _TrackingDiscountsController extends DiscountsController {
  static int refreshCalls = 0;
  static int selectRuleCalls = 0;
  static Completer<void>? refreshStarted;
  static Completer<void>? refreshGate;

  static void reset() {
    refreshCalls = 0;
    selectRuleCalls = 0;
    refreshStarted = null;
    refreshGate = null;
  }

  @override
  DiscountsState build() {
    return const DiscountsState(
      query: DiscountRulesQuery(pageSize: 20),
      isLoading: false,
    );
  }

  @override
  Future<void> refresh() async {
    refreshCalls += 1;
    refreshStarted?.complete();
    final gate = refreshGate;
    if (gate != null) {
      await gate.future;
    }
  }

  @override
  Future<void> selectRule(String ruleId) async {
    selectRuleCalls += 1;
    state = state.copyWith(selectedRuleId: ruleId);
  }
}

void _setupMocktailFallbacks() {
  registerFallbackValue(DiscountType.fixed);
}

void main() {
  setUpAll(_setupMocktailFallbacks);

  late MockPreviewDiscount mockPreview;
  late MockCreateDiscount mockCreate;
  late MockReplaceDiscount mockReplace;
  late MockDisableDiscount mockDisable;

  setUp(() {
    mockPreview = MockPreviewDiscount();
    mockCreate = MockCreateDiscount();
    mockReplace = MockReplaceDiscount();
    mockDisable = MockDisableDiscount();
    _TrackingDiscountsController.reset();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        previewDiscountProvider.overrideWithValue(mockPreview),
        createDiscountProvider.overrideWithValue(mockCreate),
        replaceDiscountProvider.overrideWithValue(mockReplace),
        disableDiscountProvider.overrideWithValue(mockDisable),
        discountsControllerProvider.overrideWith(
          _TrackingDiscountsController.new,
        ),
      ],
    );
  }

  group('DiscountEditorController', () {
    test('preview sets error when below-cost', () async {
      when(
        () => mockPreview(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => const DiscountPreview(
          totalCostReduction: 500,
          error: 'below-cost',
          estimatedProfit: -100,
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .preview(
            name: 'Clear',
            discountType: DiscountType.fixed,
            discountValue: 500,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.preview?.error, 'below-cost');
      expect(state.previewLoading, false);
    });

    test('preview stores valid result without error', () async {
      when(
        () => mockPreview(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => const DiscountPreview(
          totalCostReduction: 100,
          error: null,
          estimatedProfit: 200,
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .preview(
            name: 'Summer',
            discountType: DiscountType.percentage,
            discountValue: 10,
            batchPercentage: 0.2,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.preview?.error, null);
      expect(state.preview?.totalCostReduction, 100);
    });

    test('create sets lastAction on success', () async {
      when(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => Discount(
          discountId: 'disc-new',
          name: 'Summer',
          discountType: DiscountType.percentage,
          discountValue: 10,
          batchPercentage: 0.2,
          isEnabled: true,
          createdAt: DateTime.now(),
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      container
          .read(discountsControllerProvider.notifier)
          .state = const DiscountsState(
        query: DiscountRulesQuery(pageSize: 20),
        isLoading: false,
        selectedRuleId: 'disc-1',
      );

      // Set preview so we can verify it's cleared on success
      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 100,
          error: null,
          estimatedProfit: 200,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'Summer',
            discountType: DiscountType.percentage,
            discountValue: 10,
            batchPercentage: 0.2,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'created');
      expect(state.isSubmitting, false);
      expect(state.submitFailure, null);
      expect(state.preview, null);
      expect(_TrackingDiscountsController.refreshCalls, 1);
      expect(_TrackingDiscountsController.selectRuleCalls, 1);
    });

    test('create stores submitFailure on exception', () async {
      when(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.server(message: 'err')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'X',
            discountType: DiscountType.fixed,
            discountValue: 10,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isA<ServerFailure>());
      expect(state.lastAction, null);
    });

    test('create blocks confirmed below-cost save without reason', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 500,
          error: 'below-cost',
          estimatedProfit: -100,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'Clear',
            discountType: DiscountType.fixed,
            discountValue: 500,
            batchPercentage: null,
            confirmed: true,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isA<ValidationFailure>());
      expect(state.isSubmitting, false);
      verifyNever(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      );
    });

    test('replace sets lastAction on success', () async {
      when(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => Discount(
          discountId: 'disc-1',
          name: 'Updated',
          discountType: DiscountType.percentage,
          discountValue: 15,
          batchPercentage: null,
          isEnabled: true,
          createdAt: DateTime.now(),
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      container
          .read(discountsControllerProvider.notifier)
          .state = const DiscountsState(
        query: DiscountRulesQuery(pageSize: 20),
        isLoading: false,
        selectedRuleId: 'disc-1',
      );

      // Set preview so we can verify it's cleared on success
      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 100,
          error: null,
          estimatedProfit: 200,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'replaced');
      expect(state.isSubmitting, false);
      expect(state.preview, null);
      expect(_TrackingDiscountsController.refreshCalls, 1);
      expect(_TrackingDiscountsController.selectRuleCalls, 1);
    });

    test('disable sets lastAction on success', () async {
      when(
        () => mockDisable(discountId: any(named: 'discountId')),
      ).thenAnswer((_) async => {});

      final container = makeContainer();
      addTearDown(container.dispose);
      container
          .read(discountsControllerProvider.notifier)
          .state = const DiscountsState(
        query: DiscountRulesQuery(pageSize: 20),
        isLoading: false,
        selectedRuleId: 'disc-1',
      );

      // Set preview so we can verify it's cleared on success
      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 100,
          error: null,
          estimatedProfit: 200,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .disable(discountId: 'disc-1');

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'disabled');
      expect(state.isSubmitting, false);
      expect(state.preview, null);
      expect(_TrackingDiscountsController.refreshCalls, 1);
      expect(_TrackingDiscountsController.selectRuleCalls, 1);
    });

    test('ignores refresh follow-up when disposed mid-refresh', () async {
      when(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => Discount(
          discountId: 'disc-new',
          name: 'Summer',
          discountType: DiscountType.percentage,
          discountValue: 10,
          batchPercentage: 0.2,
          isEnabled: true,
          createdAt: DateTime.now(),
        ),
      );

      _TrackingDiscountsController.refreshStarted = Completer<void>();
      _TrackingDiscountsController.refreshGate = Completer<void>();

      final container = makeContainer();
      final createFuture = container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'Summer',
            discountType: DiscountType.percentage,
            discountValue: 10,
            batchPercentage: 0.2,
          );

      await _TrackingDiscountsController.refreshStarted!.future;
      container.dispose();
      _TrackingDiscountsController.refreshGate!.complete();

      await expectLater(createFuture, completes);
      expect(_TrackingDiscountsController.refreshCalls, 1);
      expect(_TrackingDiscountsController.selectRuleCalls, 0);
    });

    test('ignores duplicate create when already submitting', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      container.read(discountEditorControllerProvider.notifier).state =
          const DiscountEditorState(isSubmitting: true);

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'X',
            discountType: DiscountType.fixed,
            discountValue: 10,
            batchPercentage: null,
          );

      verifyNever(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      );
    });

    test(
      'create blocked when preview has below-cost error and not confirmed',
      () async {
        final container = makeContainer();
        addTearDown(container.dispose);

        container
            .read(discountEditorControllerProvider.notifier)
            .state = const DiscountEditorState(
          preview: DiscountPreview(
            totalCostReduction: 500,
            error: 'below-cost',
            estimatedProfit: -100,
          ),
        );

        await container
            .read(discountEditorControllerProvider.notifier)
            .create(
              name: 'Clear',
              discountType: DiscountType.fixed,
              discountValue: 500,
              batchPercentage: null,
              confirmed: false,
            );

        final state = container.read(discountEditorControllerProvider);
        expect(state.needsBelowCostConfirmation, true);
        verifyNever(
          () => mockCreate(
            name: any(named: 'name'),
            discountType: any(named: 'discountType'),
            discountValue: any(named: 'discountValue'),
            batchPercentage: any(named: 'batchPercentage'),
          ),
        );
      },
    );

    test('create proceeds when below-cost preview confirmed', () async {
      when(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => Discount(
          discountId: 'disc-1',
          name: 'Clear',
          discountType: DiscountType.fixed,
          discountValue: 500,
          batchPercentage: null,
          isEnabled: true,
          createdAt: DateTime.now(),
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 500,
          error: 'below-cost',
          estimatedProfit: -100,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'Clear',
            discountType: DiscountType.fixed,
            discountValue: 500,
            batchPercentage: null,
            confirmed: true,
            reason: 'seasonal clearance',
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'created');
      verify(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).called(1);
    });

    test(
      'replace blocked when preview has below-cost error and not confirmed',
      () async {
        final container = makeContainer();
        addTearDown(container.dispose);

        container
            .read(discountEditorControllerProvider.notifier)
            .state = const DiscountEditorState(
          preview: DiscountPreview(
            totalCostReduction: 500,
            error: 'below-cost',
            estimatedProfit: -100,
          ),
        );

        await container
            .read(discountEditorControllerProvider.notifier)
            .replace(
              discountId: 'disc-1',
              name: 'Updated',
              discountType: DiscountType.percentage,
              discountValue: 15,
              batchPercentage: null,
              confirmed: false,
            );

        final state = container.read(discountEditorControllerProvider);
        expect(state.needsBelowCostConfirmation, true);
        verifyNever(
          () => mockReplace(
            discountId: any(named: 'discountId'),
            name: any(named: 'name'),
            discountType: any(named: 'discountType'),
            discountValue: any(named: 'discountValue'),
            batchPercentage: any(named: 'batchPercentage'),
          ),
        );
      },
    );

    test('replace proceeds when below-cost preview confirmed', () async {
      when(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenAnswer(
        (_) async => Discount(
          discountId: 'disc-1',
          name: 'Updated',
          discountType: DiscountType.percentage,
          discountValue: 15,
          batchPercentage: null,
          isEnabled: true,
          createdAt: DateTime.now(),
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 500,
          error: 'below-cost',
          estimatedProfit: -100,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
            confirmed: true,
            reason: 'seasonal adjustment',
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'replaced');
      verify(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).called(1);
    });

    test('replace blocks confirmed below-cost save without reason', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      container
          .read(discountEditorControllerProvider.notifier)
          .state = const DiscountEditorState(
        preview: DiscountPreview(
          totalCostReduction: 500,
          error: 'below-cost',
          estimatedProfit: -100,
        ),
      );

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
            confirmed: true,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isA<ValidationFailure>());
      expect(state.isSubmitting, false);
      verifyNever(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      );
    });

    test('create stores submitFailure on generic exception', () async {
      when(
        () => mockCreate(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(Exception('Unexpected error'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .create(
            name: 'X',
            discountType: DiscountType.fixed,
            discountValue: 10,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isNotNull);
    });

    test('replace stores submitFailure on generic exception', () async {
      when(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(Exception('Unexpected error'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isNotNull);
    });

    test('ignores duplicate replace when already submitting', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      container.read(discountEditorControllerProvider.notifier).state =
          const DiscountEditorState(isSubmitting: true);

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
          );

      verifyNever(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      );
    });

    test('preview stores previewFailure on AppException', () async {
      when(
        () => mockPreview(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.server(message: 'preview err')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .preview(
            name: 'X',
            discountType: DiscountType.fixed,
            discountValue: 10,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.previewFailure, isA<ServerFailure>());
      expect(state.previewLoading, false);
    });

    test('replace stores submitFailure on AppException', () async {
      when(
        () => mockReplace(
          discountId: any(named: 'discountId'),
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.server(message: 'replace err')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .replace(
            discountId: 'disc-1',
            name: 'Updated',
            discountType: DiscountType.percentage,
            discountValue: 15,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isA<ServerFailure>());
      expect(state.isSubmitting, false);
    });

    test('disable stores submitFailure on AppException', () async {
      when(
        () => mockDisable(discountId: any(named: 'discountId')),
      ).thenThrow(
        AppException(failure: const Failure.server(message: 'disable err')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .disable(discountId: 'disc-1');

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isA<ServerFailure>());
      expect(state.isSubmitting, false);
    });

    test('disable stores submitFailure on generic exception', () async {
      when(
        () => mockDisable(discountId: any(named: 'discountId')),
      ).thenThrow(Exception('Unexpected error'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .disable(discountId: 'disc-1');

      final state = container.read(discountEditorControllerProvider);
      expect(state.submitFailure, isNotNull);
    });

    test('ignores duplicate disable when already submitting', () async {
      final container = makeContainer();
      addTearDown(container.dispose);

      container.read(discountEditorControllerProvider.notifier).state =
          const DiscountEditorState(isSubmitting: true);

      await container
          .read(discountEditorControllerProvider.notifier)
          .disable(discountId: 'disc-1');

      verifyNever(
        () => mockDisable(discountId: any(named: 'discountId')),
      );
    });

    test('preview stores previewFailure on generic exception', () async {
      when(
        () => mockPreview(
          name: any(named: 'name'),
          discountType: any(named: 'discountType'),
          discountValue: any(named: 'discountValue'),
          batchPercentage: any(named: 'batchPercentage'),
        ),
      ).thenThrow(Exception('network'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .preview(
            name: 'X',
            discountType: DiscountType.fixed,
            discountValue: 10,
            batchPercentage: null,
          );

      final state = container.read(discountEditorControllerProvider);
      expect(state.previewFailure, isNotNull);
      expect(state.previewLoading, false);
    });
  });
}
