import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discount_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/repositories/discount_repository_impl.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/create_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/disable_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/preview_discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/replace_discount.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'discount_editor_controller.g.dart';

@riverpod
DiscountRemoteDataSource discountRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return DiscountRemoteDataSourceImpl(apiClient);
}

@riverpod
DiscountRepository discountRepository(Ref ref) {
  final remoteDataSource = ref.watch(discountRemoteDataSourceProvider);
  return DiscountRepositoryImpl(remoteDataSource);
}

@riverpod
PreviewDiscount previewDiscount(Ref ref) {
  final repository = ref.watch(discountRepositoryProvider);
  return PreviewDiscount(repository);
}

@riverpod
CreateDiscount createDiscount(Ref ref) {
  final repository = ref.watch(discountRepositoryProvider);
  return CreateDiscount(repository);
}

@riverpod
ReplaceDiscount replaceDiscount(Ref ref) {
  final repository = ref.watch(discountRepositoryProvider);
  return ReplaceDiscount(repository);
}

@riverpod
DisableDiscount disableDiscount(Ref ref) {
  final repository = ref.watch(discountRepositoryProvider);
  return DisableDiscount(repository);
}

class DiscountEditorState {
  static const _keepValue = Object();

  const DiscountEditorState({
    this.preview,
    this.previewLoading = false,
    this.previewFailure,
    this.isSubmitting = false,
    this.submitFailure,
    this.lastAction,
    this.needsBelowCostConfirmation = false,
  });

  final DiscountPreview? preview;
  final bool previewLoading;
  final Failure? previewFailure;
  final bool isSubmitting;
  final Failure? submitFailure;
  final String? lastAction;
  final bool needsBelowCostConfirmation;

  DiscountEditorState copyWith({
    Object? preview = _keepValue,
    bool? previewLoading,
    Failure? previewFailure,
    bool? isSubmitting,
    Failure? submitFailure,
    String? lastAction,
    bool? needsBelowCostConfirmation,
  }) => DiscountEditorState(
    preview: identical(preview, _keepValue)
        ? this.preview
        : preview as DiscountPreview?,
    previewLoading: previewLoading ?? this.previewLoading,
    previewFailure: previewFailure,
    isSubmitting: isSubmitting ?? this.isSubmitting,
    submitFailure: submitFailure,
    lastAction: lastAction,
    needsBelowCostConfirmation:
        needsBelowCostConfirmation ?? this.needsBelowCostConfirmation,
  );
}

@riverpod
class DiscountEditorController extends _$DiscountEditorController {
  @override
  DiscountEditorState build() {
    return const DiscountEditorState();
  }

  Future<void> preview({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) async {
    state = state.copyWith(previewLoading: true, previewFailure: null);
    try {
      final result = await ref.read(previewDiscountProvider)(
        name: name,
        discountType: discountType,
        discountValue: discountValue,
        batchPercentage: batchPercentage,
      );
      state = state.copyWith(preview: result, previewLoading: false);
    } on AppException catch (e) {
      state = state.copyWith(
        previewLoading: false,
        previewFailure: e.failure,
      );
    } catch (e) {
      state = state.copyWith(
        previewLoading: false,
        previewFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> create({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
    bool confirmed = false,
    String? reason,
  }) async {
    if (state.isSubmitting) return;
    if (state.preview?.error != null && !confirmed) {
      state = state.copyWith(
        needsBelowCostConfirmation: true,
      );
      return;
    }
    if (state.preview?.error != null && confirmed && _isBlank(reason)) {
      state = state.copyWith(
        submitFailure: const Failure.validation(
          message: 'Reason required for below-cost confirmation',
        ),
      );
      return;
    }
    state = state.copyWith(
      isSubmitting: true,
      submitFailure: null,
      needsBelowCostConfirmation: false,
    );
    try {
      // reason is UI-only (local audit/confirmation context), not forwarded to backend
      await ref.read(createDiscountProvider)(
        name: name,
        discountType: discountType,
        discountValue: discountValue,
        batchPercentage: batchPercentage,
      );
      state = state.copyWith(
        isSubmitting: false,
        lastAction: 'created',
        preview: null,
      );
    } on AppException catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: e.failure,
      );
    } catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> replace({
    required String discountId,
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
    bool confirmed = false,
    String? reason,
  }) async {
    if (state.isSubmitting) return;
    if (state.preview?.error != null && !confirmed) {
      state = state.copyWith(
        needsBelowCostConfirmation: true,
      );
      return;
    }
    if (state.preview?.error != null && confirmed && _isBlank(reason)) {
      state = state.copyWith(
        submitFailure: const Failure.validation(
          message: 'Reason required for below-cost confirmation',
        ),
      );
      return;
    }
    state = state.copyWith(
      isSubmitting: true,
      submitFailure: null,
      needsBelowCostConfirmation: false,
    );
    try {
      // reason is UI-only (local audit/confirmation context), not forwarded to backend
      await ref.read(replaceDiscountProvider)(
        discountId: discountId,
        name: name,
        discountType: discountType,
        discountValue: discountValue,
        batchPercentage: batchPercentage,
      );
      state = state.copyWith(
        isSubmitting: false,
        lastAction: 'replaced',
        preview: null,
      );
    } on AppException catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: e.failure,
      );
    } catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> disable({required String discountId}) async {
    if (state.isSubmitting) return;
    state = state.copyWith(isSubmitting: true, submitFailure: null);
    try {
      await ref.read(disableDiscountProvider)(discountId: discountId);
      state = state.copyWith(
        isSubmitting: false,
        lastAction: 'disabled',
        preview: null,
      );
    } on AppException catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: e.failure,
      );
    } catch (e) {
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }

  bool _isBlank(String? value) => value == null || value.trim().isEmpty;
}
