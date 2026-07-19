import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';

void main() {
  final l10n = lookupAppLocalizations(const Locale('en', 'IN'));

  test('maps every PO status to generated localization', () {
    final messages = {
      for (final status in PurchaseOrderStatus.values)
        purchaseOrderStatusMessage(l10n, status),
    };

    expect(messages, hasLength(PurchaseOrderStatus.values.length));
    expect(messages, containsAll(<String>{'Draft', 'Partially received'}));
  });

  test('maps confirmed failure classes without exposing raw messages', () {
    const raw = 'sensitive server diagnostic';
    final cases = <Failure, String>{
      const Failure.validation(message: raw):
          l10n.purchaseOrderFailureValidation,
      const Failure.unauthorized(message: raw):
          l10n.purchaseOrderFailureUnauthorized,
      const Failure.forbidden(message: raw): l10n.purchaseOrderFailureForbidden,
      const Failure.notFound(message: raw): l10n.purchaseOrderFailureNotFound,
      const Failure.server(message: raw, statusCode: 409):
          l10n.purchaseOrderFailureLifecycleConflict,
      const Failure.server(message: raw, statusCode: 500):
          l10n.purchaseOrderFailureServer,
      const Failure.network(message: raw): l10n.purchaseOrderFailureNetwork,
      const Failure.timeout(message: raw): l10n.purchaseOrderFailureTimeout,
      const Failure.serialization(message: raw):
          l10n.purchaseOrderFailureSerialization,
      const Failure.unknown(message: raw): l10n.purchaseOrderFailureUnknown,
    };

    for (final MapEntry(key: failure, value: expected) in cases.entries) {
      final actual = purchaseOrderFailureMessage(l10n, failure);
      expect(actual, expected);
      expect(actual, isNot(contains(raw)));
    }
  });

  test('maps every typed local state to generated localization', () {
    for (final message in PurchaseOrderMessage.values) {
      final localized = purchaseOrderMessage(
        l10n,
        message,
        maxLength: message == PurchaseOrderMessage.receiveMaxLength ? 42 : null,
      );
      expect(localized, isNotEmpty, reason: message.name);
    }
    expect(
      purchaseOrderMessage(
        l10n,
        PurchaseOrderMessage.receiveMaxLength,
        maxLength: 42,
      ),
      contains('42'),
    );
  });
}
