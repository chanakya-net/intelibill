import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'sale_return_controller.g.dart';

@immutable
class SaleReturnState {
  const SaleReturnState({
    required this.saleId,
    this.detail,
    this.preview,
    this.isPreviewLoading = false,
    this.isSubmitting = false,
    this.dueReductionOverrideAmount,
    this.dueOverrideReason,
    this.dueOverrideConfirmed = false,
    this.payoutDestination,
    this.notes,
    this.creditNoteReason,
    this.creditNoteExpiresAt,
    this.drafts = const [],
    this.failure,
    this.session,
  });

  final String saleId;
  final SaleDetail? detail;
  final SaleReturnPreview? preview;
  final bool isPreviewLoading;
  final bool isSubmitting;
  final double? dueReductionOverrideAmount;
  final String? dueOverrideReason;
  final bool dueOverrideConfirmed;
  final int? payoutDestination;
  final String? notes;
  final String? creditNoteReason;
  final DateTime? creditNoteExpiresAt;
  final List<SaleReturnLineDraft> drafts;
  final Failure? failure;
  final AuthSession? session;

  bool get hasAnyReturnable =>
      detail?.items.any((item) => item.returnableQuantity > 0) ?? false;

  List<SaleReturnLineDraft> get selectedDrafts =>
      drafts.where((item) => item.selected).toList();

  bool get canPreviewReturns =>
      detail != null && hasAnyReturnable && selectedDrafts.isNotEmpty;

  bool get canSubmitReturns => isOwnerOrManager(session);

  SaleReturnState copyWith({
    SaleDetail? detail,
    SaleReturnPreview? preview,
    bool? isPreviewLoading,
    bool? isSubmitting,
    double? dueReductionOverrideAmount,
    String? dueOverrideReason,
    bool? dueOverrideConfirmed,
    int? payoutDestination,
    String? notes,
    String? creditNoteReason,
    DateTime? creditNoteExpiresAt,
    List<SaleReturnLineDraft>? drafts,
    Failure? failure,
    AuthSession? session,
    bool clearFailure = false,
    bool clearDueReductionOverrideAmount = false,
    bool clearDueOverrideReason = false,
    bool clearPayoutDestination = false,
    bool clearNotes = false,
    bool clearCreditNoteReason = false,
    bool clearCreditNoteExpiresAt = false,
  }) {
    return SaleReturnState(
      saleId: saleId,
      detail: detail ?? this.detail,
      preview: preview ?? this.preview,
      isPreviewLoading: isPreviewLoading ?? this.isPreviewLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      dueReductionOverrideAmount: clearDueReductionOverrideAmount
          ? null
          : (dueReductionOverrideAmount ?? this.dueReductionOverrideAmount),
      dueOverrideReason: clearDueOverrideReason
          ? null
          : (dueOverrideReason ?? this.dueOverrideReason),
      dueOverrideConfirmed: dueOverrideConfirmed ?? this.dueOverrideConfirmed,
      payoutDestination: clearPayoutDestination
          ? null
          : (payoutDestination ?? this.payoutDestination),
      notes: clearNotes ? null : (notes ?? this.notes),
      creditNoteReason: clearCreditNoteReason
          ? null
          : (creditNoteReason ?? this.creditNoteReason),
      creditNoteExpiresAt: clearCreditNoteExpiresAt
          ? null
          : (creditNoteExpiresAt ?? this.creditNoteExpiresAt),
      drafts: drafts ?? this.drafts,
      failure: clearFailure ? null : (failure ?? this.failure),
      session: session ?? this.session,
    );
  }
}

@riverpod
class SaleReturnController extends _$SaleReturnController {
  static const _serviceLineType = 'Service';
  static const _conditionRestockable = 1;
  static const _payoutDestinationCreditNote = 1;
  static const _payoutDestinationRefund = 2;
  static const _payoutMethodRefund = 2;

  @override
  SaleReturnState build(String saleId) {
    final detail = ref.watch(saleDetailControllerProvider(saleId)).detail;
    final session = ref.watch(authControllerProvider).value?.session;

    return SaleReturnState(
      saleId: saleId,
      detail: detail,
      drafts: _buildDrafts(detail),
      session: session,
    );
  }

  void resetDrafts() {
    state = state.copyWith(
      detail: state.detail,
      drafts: _buildDrafts(state.detail),
      preview: null,
      clearDueOverrideReason: true,
      clearDueReductionOverrideAmount: true,
      clearPayoutDestination: true,
      clearNotes: true,
      clearCreditNoteReason: true,
      clearCreditNoteExpiresAt: true,
      dueOverrideConfirmed: false,
      clearFailure: true,
    );
  }

  void toggleLine(String saleItemId, bool selected) {
    final draft = _draftFor(saleItemId);
    final item = _itemFor(saleItemId);
    if (draft == null || item == null) return;

    final next = draft.copyWith(
      selected: selected,
      quantity: selected ? item.returnableQuantity : 0,
      condition: selected
          ? (item.lineType == _serviceLineType ? null : _conditionRestockable)
          : null,
      clearApprovedRefundAmount: true,
      clearNotes: true,
      clearCondition: selected ? (item.lineType == _serviceLineType) : false,
    );

    state = state.copyWith(
      drafts: _replaceDraft(next),
      clearFailure: true,
      clearCreditNoteExpiresAt: true,
      clearCreditNoteReason: true,
      clearPayoutDestination: true,
    );
  }

  void updateQuantity(String saleItemId, double quantity) {
    final draft = _draftFor(saleItemId);
    final item = _itemFor(saleItemId);
    if (draft == null || item == null) return;

    final value = _clamp(quantity, 0, item.returnableQuantity);
    final selected = value > 0;
    state = state.copyWith(
      drafts: _replaceDraft(
        draft.copyWith(
          selected: selected,
          quantity: value,
          clearCondition: selected
              ? (item.lineType == _serviceLineType
                    ? false
                    : draft.condition == null)
              : false,
        ),
      ),
      clearFailure: true,
    );
  }

  void updateCondition(String saleItemId, int? condition) {
    final draft = _draftFor(saleItemId);
    if (draft == null) return;
    state = state.copyWith(
      drafts: _replaceDraft(draft.copyWith(condition: condition)),
      clearFailure: true,
    );
  }

  void updateApprovedRefundAmount(String saleItemId, String value) {
    final draft = _draftFor(saleItemId);
    if (draft == null) return;
    final parsed = double.tryParse(value.trim());
    state = state.copyWith(
      drafts: _replaceDraft(
        draft.copyWith(
          approvedRefundAmount: parsed == null || !parsed.isFinite || parsed < 0
              ? null
              : parsed,
        ),
      ),
      clearFailure: true,
    );
  }

  void updateLineNotes(String saleItemId, String value) {
    final draft = _draftFor(saleItemId);
    if (draft == null) return;
    final trimmed = value.trim();
    state = state.copyWith(
      drafts: _replaceDraft(
        draft.copyWith(
          notes: trimmed.isEmpty ? null : trimmed,
          clearNotes: trimmed.isEmpty,
        ),
      ),
      clearFailure: true,
    );
  }

  void updateDueReductionAmount(String value) {
    final trimmed = value.trim();
    final parsed = double.tryParse(trimmed);
    state = state.copyWith(
      dueReductionOverrideAmount: parsed,
      dueOverrideConfirmed: false,
      clearDueReductionOverrideAmount: trimmed.isEmpty,
      clearFailure: true,
    );
  }

  void updateDueOverrideReason(String value) {
    final trimmed = value.trim();
    state = state.copyWith(
      dueOverrideReason: trimmed.isEmpty ? null : trimmed,
      clearDueOverrideReason: trimmed.isEmpty,
      dueOverrideConfirmed: false,
      clearFailure: true,
    );
  }

  void updateDueOverrideConfirmed(bool value) {
    state = state.copyWith(dueOverrideConfirmed: value, clearFailure: true);
  }

  void updatePayoutDestination(int? value) {
    state = state.copyWith(
      payoutDestination: value,
      clearPayoutDestination: value == null,
      clearCreditNoteReason: value != _payoutDestinationCreditNote,
      clearCreditNoteExpiresAt: value != _payoutDestinationCreditNote,
      clearFailure: true,
    );
  }

  void updateNotes(String value) {
    final trimmed = value.trim();
    state = state.copyWith(
      notes: trimmed.isEmpty ? null : trimmed,
      clearNotes: trimmed.isEmpty,
      clearFailure: true,
    );
  }

  void updateCreditNoteReason(String value) {
    final trimmed = value.trim();
    state = state.copyWith(
      creditNoteReason: trimmed.isEmpty ? null : trimmed,
      clearCreditNoteReason: trimmed.isEmpty,
      clearFailure: true,
    );
  }

  void updateCreditNoteExpiresAt(DateTime? value) {
    state = state.copyWith(
      creditNoteExpiresAt: value,
      clearCreditNoteExpiresAt: value == null,
      clearFailure: true,
    );
  }

  Future<void> preview() async {
    if (!state.canPreviewReturns) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'Select at least one returnable line.',
        ),
      );
      return;
    }

    final errors = _validationErrors(requireFinancial: false);
    if (errors.isNotEmpty) {
      state = state.copyWith(
        failure: Failure.validation(message: errors.join('\n')),
      );
      return;
    }

    final request = _previewRequest();
    if (request == null) return;

    state = state.copyWith(isPreviewLoading: true, clearFailure: true);
    try {
      final preview = await ref.read(previewSaleReturnProvider)(
        saleId: state.saleId,
        request: request,
      );
      state = state.copyWith(preview: preview, isPreviewLoading: false);
    } on AppException catch (error) {
      state = state.copyWith(isPreviewLoading: false, failure: error.failure);
    } on Object {
      state = state.copyWith(
        isPreviewLoading: false,
        failure: const Failure.unknown(
          message: 'Unable to calculate sale return preview.',
        ),
      );
    }
  }

  Future<void> submit() async {
    if (!state.canSubmitReturns) {
      state = state.copyWith(
        failure: const Failure.forbidden(
          message: 'Only owner or manager can record sale returns.',
        ),
      );
      return;
    }

    if (!state.canPreviewReturns) {
      state = state.copyWith(
        failure: const Failure.validation(message: 'Select at least one line.'),
      );
      return;
    }

    final errors = _validationErrors(requireFinancial: true);
    if (errors.isNotEmpty) {
      state = state.copyWith(
        failure: Failure.validation(message: errors.join('\n')),
      );
      return;
    }

    final request = _recordRequest();
    if (request == null) return;

    state = state.copyWith(isSubmitting: true, clearFailure: true);
    try {
      await ref.read(recordSaleReturnProvider)(
        saleId: state.saleId,
        request: request,
      );
      try {
        await ref
            .read(saleDetailControllerProvider(state.saleId).notifier)
            .refresh();
      } on Object {
        // Refreshing the sheet content after a successful submission should not block
        // recording the return; stale values can be corrected by re-opening details.
      }
      try {
        await ref.read(salesHistoryControllerProvider.notifier).refresh();
      } on Object {
        // Keep the recorded return even if the history list cannot be refreshed immediately.
      }

      if (!ref.mounted) return;

      state = state.copyWith(
        isSubmitting: false,
        preview: null,
        drafts: _buildDrafts(state.detail),
        clearDueOverrideReason: true,
        clearDueReductionOverrideAmount: true,
        clearPayoutDestination: true,
        clearNotes: true,
        clearCreditNoteReason: true,
        clearCreditNoteExpiresAt: true,
        dueOverrideConfirmed: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, failure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        failure: const Failure.unknown(
          message: 'Unable to record sale return.',
        ),
      );
    }
  }

  PreviewSaleReturnRequest? _previewRequest() {
    if (state.selectedDrafts.isEmpty) return null;
    return PreviewSaleReturnRequest(
      dueReductionOverrideAmount: state.dueReductionOverrideAmount,
      dueOverrideReason: state.dueOverrideReason,
      items: state.selectedDrafts,
    );
  }

  RecordSaleReturnRequest? _recordRequest() {
    if (state.selectedDrafts.isEmpty) return null;
    return RecordSaleReturnRequest(
      payoutDestination: state.payoutDestination,
      // Backend currently expects payoutMethod to use the same numeric value as the
      // refund destination.
      payoutMethod: state.payoutDestination == _payoutDestinationRefund
          ? _payoutMethodRefund
          : null,
      dueReductionOverrideAmount: state.dueReductionOverrideAmount,
      dueOverrideReason: state.dueOverrideReason,
      notes: state.notes,
      creditNoteExpiresAt: state.creditNoteExpiresAt?.toIso8601String(),
      creditNoteReason: state.creditNoteReason,
      items: state.selectedDrafts,
    );
  }

  List<String> _validationErrors({required bool requireFinancial}) {
    if (state.detail == null) {
      return const ['Sale detail is not loaded.'];
    }

    if (state.selectedDrafts.isEmpty) {
      return const ['Select at least one return line.'];
    }

    final detailLineById = {
      for (final item in state.detail!.items) item.saleItemId: item,
    };
    final errors = <String>[];

    for (final draft in state.selectedDrafts) {
      final item = detailLineById[draft.saleItemId];
      if (item == null) {
        errors.add('Selected line is no longer available.');
        continue;
      }
      if (draft.quantity <= 0) {
        errors.add('Return quantity must be greater than zero.');
      }
      if (draft.quantity > item.returnableQuantity) {
        errors.add('Return quantity exceeds returnable quantity.');
      }
      if (item.lineType != _serviceLineType && draft.condition == null) {
        errors.add('Select condition for goods lines.');
      }
      if (requireFinancial && draft.approvedRefundAmount == null) {
        errors.add('Approved refund amount is required.');
      }
    }

    final hasPayoutReduction =
        (state.dueReductionOverrideAmount ?? 0).abs() > 0.0001;
    if (hasPayoutReduction &&
        (state.dueOverrideReason == null ||
            state.dueOverrideReason!.trim().isEmpty)) {
      errors.add('Due reduction reason is required.');
    }

    if (requireFinancial &&
        state.dueReductionOverrideAmount != null &&
        !state.dueOverrideConfirmed) {
      errors.add('Confirm due reduction override before recording.');
    }

    if (requireFinancial && state.payoutDestination == null) {
      errors.add('Choose payout destination.');
    }

    if (state.payoutDestination == _payoutDestinationCreditNote) {
      if (state.creditNoteReason == null ||
          state.creditNoteReason!.trim().isEmpty) {
        errors.add('Credit note reason is required.');
      }
      if (state.creditNoteExpiresAt == null) {
        errors.add('Credit note expiry is required.');
      }
    }

    return errors;
  }

  List<SaleReturnLineDraft> _buildDrafts(SaleDetail? detail) {
    if (detail == null) return const [];

    return detail.items
        .where((item) => item.returnableQuantity > 0)
        .map(
          (item) => SaleReturnLineDraft(
            saleItemId: item.saleItemId,
            selected: false,
            quantity: 0,
            lineType: item.lineType,
            condition: item.lineType == _serviceLineType
                ? null
                : _conditionRestockable,
            approvedRefundAmount: null,
            notes: null,
          ),
        )
        .toList();
  }

  SaleDetailItem? _itemFor(String saleItemId) {
    final detail = state.detail;
    if (detail == null) return null;

    for (final item in detail.items) {
      if (item.saleItemId == saleItemId) {
        return item;
      }
    }
    return null;
  }

  SaleReturnLineDraft? _draftFor(String saleItemId) {
    for (final draft in state.drafts) {
      if (draft.saleItemId == saleItemId) return draft;
    }
    return null;
  }

  List<SaleReturnLineDraft> _replaceDraft(SaleReturnLineDraft draft) {
    return [
      for (final item in state.drafts)
        if (item.saleItemId == draft.saleItemId) draft else item,
    ];
  }

  double _clamp(double value, double min, double max) {
    if (!value.isFinite) return min;
    if (value < min) return min;
    if (value > max) return max;
    return value;
  }
}
