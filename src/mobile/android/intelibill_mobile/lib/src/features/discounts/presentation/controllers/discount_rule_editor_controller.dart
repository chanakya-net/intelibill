import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'discount_rule_editor_controller.g.dart';

@riverpod
CreateDiscountRule createDiscountRule(Ref ref) {
  final repository = ref.watch(discountsRepositoryProvider);
  return CreateDiscountRule(repository);
}

@riverpod
PreviewDiscountRule previewDiscountRule(Ref ref) {
  final repository = ref.watch(discountsRepositoryProvider);
  return PreviewDiscountRule(repository);
}

@immutable
class DiscountRuleEditorState {
  const DiscountRuleEditorState({
    this.batches = const [],
    this.isLoadingBatches = false,
    this.batchesFailure,
    this.preview,
    this.isPreviewLoading = false,
    this.previewFailure,
    this.isSubmitting = false,
    this.submitFailure,
    this.localValidationMessage,
  });

  final List<InventoryBatch> batches;
  final bool isLoadingBatches;
  final Failure? batchesFailure;
  final DiscountRulePreview? preview;
  final bool isPreviewLoading;
  final Failure? previewFailure;
  final bool isSubmitting;
  final Failure? submitFailure;
  final String? localValidationMessage;

  List<InventoryBatch> get selectableBatches =>
      batches.where((batch) => !batch.isVoided).toList();

  DiscountRuleEditorState copyWith({
    List<InventoryBatch>? batches,
    bool? isLoadingBatches,
    Failure? batchesFailure,
    bool clearBatchesFailure = false,
    DiscountRulePreview? preview,
    bool clearPreview = false,
    bool? isPreviewLoading,
    Failure? previewFailure,
    bool clearPreviewFailure = false,
    bool? isSubmitting,
    Failure? submitFailure,
    bool clearSubmitFailure = false,
    String? localValidationMessage,
    bool clearLocalValidationMessage = false,
  }) {
    return DiscountRuleEditorState(
      batches: batches ?? this.batches,
      isLoadingBatches: isLoadingBatches ?? this.isLoadingBatches,
      batchesFailure: clearBatchesFailure
          ? null
          : (batchesFailure ?? this.batchesFailure),
      preview: clearPreview ? null : (preview ?? this.preview),
      isPreviewLoading: isPreviewLoading ?? this.isPreviewLoading,
      previewFailure: clearPreviewFailure
          ? null
          : (previewFailure ?? this.previewFailure),
      isSubmitting: isSubmitting ?? this.isSubmitting,
      submitFailure: clearSubmitFailure
          ? null
          : (submitFailure ?? this.submitFailure),
      localValidationMessage: clearLocalValidationMessage
          ? null
          : (localValidationMessage ?? this.localValidationMessage),
    );
  }
}

@riverpod
class DiscountRuleEditorController extends _$DiscountRuleEditorController {
  @override
  DiscountRuleEditorState build() {
    return const DiscountRuleEditorState();
  }

  Future<void> loadBatches({bool force = false}) async {
    if (state.isLoadingBatches) return;
    if (!force && state.batches.isNotEmpty) return;

    state = state.copyWith(
      isLoadingBatches: true,
      clearBatchesFailure: true,
    );

    try {
      final batches = await ref.read(getInventoryBatchesProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(
        batches: batches,
        isLoadingBatches: false,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingBatches: false,
        batchesFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingBatches: false,
        batchesFailure: const Failure.unknown(),
      );
    }
  }

  Future<DiscountRulePreview?> preview(PreviewDiscountRuleInput input) async {
    if (state.isPreviewLoading || state.isSubmitting) return null;

    state = state.copyWith(
      isPreviewLoading: true,
      clearPreviewFailure: true,
      clearSubmitFailure: true,
      clearLocalValidationMessage: true,
    );

    try {
      final preview = await ref.read(previewDiscountRuleProvider)(input);
      if (!ref.mounted) return null;
      state = state.copyWith(
        preview: preview,
        isPreviewLoading: false,
      );
      return preview;
    } on AppException catch (error) {
      if (!ref.mounted) return null;
      state = state.copyWith(
        isPreviewLoading: false,
        previewFailure: error.failure,
        clearPreview: true,
      );
      return null;
    } on Object {
      if (!ref.mounted) return null;
      state = state.copyWith(
        isPreviewLoading: false,
        previewFailure: const Failure.unknown(),
        clearPreview: true,
      );
      return null;
    }
  }

  Future<bool> create(CreateDiscountRuleInput input) async {
    if (state.isSubmitting || state.isPreviewLoading) return false;

    state = state.copyWith(
      clearSubmitFailure: true,
      clearLocalValidationMessage: true,
    );

    final preview =
        state.preview ??
        await this.preview(
          PreviewDiscountRuleInput(
            ruleType: input.ruleType,
            percentage: input.percentage,
            thresholdAmount: input.thresholdAmount,
            inventoryBatchId: input.inventoryBatchId,
            startsAt: input.startsAt,
            endsAt: input.endsAt,
            belowCostConfirmed: input.belowCostConfirmed,
          ),
        );

    if (preview == null) return false;
    if (preview.hasErrors) {
      state = state.copyWith(
        localValidationMessage: preview.errors.first.message,
      );
      return false;
    }

    if (preview.needsBelowCostConfirmation && !input.belowCostConfirmed) {
      state = state.copyWith(
        localValidationMessage: 'belowCostConfirmationRequired',
      );
      return false;
    }

    if (preview.needsBelowCostConfirmation &&
        input.belowCostConfirmed &&
        (input.belowCostConfirmationReason == null ||
            input.belowCostConfirmationReason!.trim().isEmpty)) {
      state = state.copyWith(
        localValidationMessage: 'belowCostReasonRequired',
      );
      return false;
    }

    state = state.copyWith(isSubmitting: true);

    try {
      await ref.read(createDiscountRuleProvider)(input);
      if (!ref.mounted) return false;
      await ref.read(discountsControllerProvider.notifier).refresh();
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        clearPreview: true,
      );
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: error.failure,
      );
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
      return false;
    }
  }

  void clearMessages() {
    state = state.copyWith(
      clearSubmitFailure: true,
      clearPreviewFailure: true,
      clearLocalValidationMessage: true,
    );
  }
}
