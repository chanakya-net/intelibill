import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/disable_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/replace_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discount_editor_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockPreviewDiscount extends Mock implements PreviewDiscount {}

class MockCreateDiscount extends Mock implements CreateDiscount {}

class MockReplaceDiscount extends Mock implements ReplaceDiscount {}

class MockDisableDiscount extends Mock implements DisableDiscount {}

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
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        previewDiscountProvider.overrideWithValue(mockPreview),
        createDiscountProvider.overrideWithValue(mockCreate),
        replaceDiscountProvider.overrideWithValue(mockReplace),
        disableDiscountProvider.overrideWithValue(mockDisable),
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
    });

    test('disable sets lastAction on success', () async {
      when(
        () => mockDisable(discountId: any(named: 'discountId')),
      ).thenAnswer((_) async => {});

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(discountEditorControllerProvider.notifier)
          .disable(discountId: 'disc-1');

      final state = container.read(discountEditorControllerProvider);
      expect(state.lastAction, 'disabled');
      expect(state.isSubmitting, false);
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
        expect(state.submitFailure, isNotNull);
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
        expect(state.submitFailure, isNotNull);
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
  });
}
