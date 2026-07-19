import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

enum PurchaseOrderMessage {
  builderOrderDates,
  builderQuantity,
  builderUnitCost,
  builderDescription,
  builderReference,
  builderNotes,
  draftSaveStorage,
  draftRemoveStorage,
  draftPlaceCleanup,
  draftSaveCleanup,
  receivePositiveWholeQuantity,
  receiveNoRemainingLines,
  receiveSelectLine,
  receiveDuplicateLine,
  receiveInvalidNumber,
  receiveQuantityRange,
  receiveSalesPrice,
  receiveTaxRate,
  receiveExpiryDate,
  receiveRequired,
  receiveMaxLength,
  receiveNonNegative,
  receiveFieldRejected,
  receiveFixFields,
  receiveBarcodeBuild,
  receiveBarcodeLineBuild,
  receivePrefill,
}

String purchaseOrderStatusMessage(
  AppLocalizations l10n,
  PurchaseOrderStatus status,
) => switch (status) {
  PurchaseOrderStatus.draft => l10n.purchaseOrderStatusDraft,
  PurchaseOrderStatus.placed => l10n.purchaseOrderStatusPlaced,
  PurchaseOrderStatus.partiallyReceived =>
    l10n.purchaseOrderStatusPartiallyReceived,
  PurchaseOrderStatus.received => l10n.purchaseOrderStatusReceived,
  PurchaseOrderStatus.cancelled => l10n.purchaseOrderStatusCancelled,
  PurchaseOrderStatus.closed => l10n.purchaseOrderStatusClosed,
};

String purchaseOrderFailureMessage(AppLocalizations l10n, Failure failure) {
  if (failure is ServerFailure && failure.statusCode == 409) {
    return l10n.purchaseOrderFailureLifecycleConflict;
  }
  return switch (failure) {
    ValidationFailure() => l10n.purchaseOrderFailureValidation,
    UnauthorizedFailure() => l10n.purchaseOrderFailureUnauthorized,
    ForbiddenFailure() => l10n.purchaseOrderFailureForbidden,
    NotFoundFailure() => l10n.purchaseOrderFailureNotFound,
    ServerFailure() => l10n.purchaseOrderFailureServer,
    NetworkFailure() => l10n.purchaseOrderFailureNetwork,
    TimeoutFailure() => l10n.purchaseOrderFailureTimeout,
    SerializationFailure() => l10n.purchaseOrderFailureSerialization,
    UnknownFailure() => l10n.purchaseOrderFailureUnknown,
  };
}

String purchaseOrderMessage(
  AppLocalizations l10n,
  PurchaseOrderMessage message, {
  int? maxLength,
}) => switch (message) {
  PurchaseOrderMessage.builderOrderDates =>
    l10n.purchaseOrderBuilderValidationOrderDates,
  PurchaseOrderMessage.builderQuantity =>
    l10n.purchaseOrderBuilderValidationQuantity,
  PurchaseOrderMessage.builderUnitCost =>
    l10n.purchaseOrderBuilderValidationUnitCost,
  PurchaseOrderMessage.builderDescription =>
    l10n.purchaseOrderBuilderValidationDescription,
  PurchaseOrderMessage.builderReference =>
    l10n.purchaseOrderBuilderValidationReference,
  PurchaseOrderMessage.builderNotes => l10n.purchaseOrderBuilderValidationNotes,
  PurchaseOrderMessage.draftSaveStorage =>
    l10n.purchaseOrderDraftSaveStorageFailure,
  PurchaseOrderMessage.draftRemoveStorage =>
    l10n.purchaseOrderDraftRemoveStorageFailure,
  PurchaseOrderMessage.draftPlaceCleanup =>
    l10n.purchaseOrderDraftPlaceCleanupFailure,
  PurchaseOrderMessage.draftSaveCleanup =>
    l10n.purchaseOrderDraftSaveCleanupFailure,
  PurchaseOrderMessage.receivePositiveWholeQuantity =>
    l10n.purchaseOrderReceiveFailurePositiveWholeQuantity,
  PurchaseOrderMessage.receiveNoRemainingLines =>
    l10n.purchaseOrderReceiveFailureNoRemainingLines,
  PurchaseOrderMessage.receiveSelectLine =>
    l10n.purchaseOrderReceiveFailureSelectLine,
  PurchaseOrderMessage.receiveDuplicateLine =>
    l10n.purchaseOrderReceiveFailureDuplicateLine,
  PurchaseOrderMessage.receiveInvalidNumber =>
    l10n.purchaseOrderReceiveFailureInvalidNumber,
  PurchaseOrderMessage.receiveQuantityRange =>
    l10n.purchaseOrderReceiveFailureQuantityRange,
  PurchaseOrderMessage.receiveSalesPrice =>
    l10n.purchaseOrderReceiveFailureSalesPrice,
  PurchaseOrderMessage.receiveTaxRate =>
    l10n.purchaseOrderReceiveFailureTaxRate,
  PurchaseOrderMessage.receiveExpiryDate =>
    l10n.purchaseOrderReceiveFailureExpiryDate,
  PurchaseOrderMessage.receiveRequired =>
    l10n.purchaseOrderReceiveFailureRequired,
  PurchaseOrderMessage.receiveMaxLength =>
    l10n.purchaseOrderReceiveFailureMaxLength(maxLength ?? 0),
  PurchaseOrderMessage.receiveNonNegative =>
    l10n.purchaseOrderReceiveFailureNonNegative,
  PurchaseOrderMessage.receiveFieldRejected =>
    l10n.purchaseOrderReceiveFailureFieldRejected,
  PurchaseOrderMessage.receiveFixFields =>
    l10n.purchaseOrderReceiveFailureFixFields,
  PurchaseOrderMessage.receiveBarcodeBuild =>
    l10n.purchaseOrderReceiveBarcodeBuildFailure,
  PurchaseOrderMessage.receiveBarcodeLineBuild =>
    l10n.purchaseOrderReceiveBarcodeLineBuildFailure,
  PurchaseOrderMessage.receivePrefill =>
    l10n.purchaseOrderReceivePrefillFailure,
};

class PurchaseOrderFieldMessage {
  const PurchaseOrderFieldMessage(this.code, {this.maxLength});

  final PurchaseOrderMessage code;
  final int? maxLength;
}
