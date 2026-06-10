import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

String salePaymentMethodLabel(AppLocalizations l10n, int paymentMethod) {
  switch (paymentMethod) {
    case 1:
      return l10n.salesHistoryPaymentCash;
    case 2:
      return l10n.salesHistoryPaymentUpi;
    case 3:
      return l10n.salesHistoryPaymentCard;
    case 4:
      return l10n.salesHistoryPaymentCredit;
    default:
      return l10n.salesHistoryPaymentUnknown;
  }
}

String saleStatusLabel(AppLocalizations l10n, String status) {
  switch (status) {
    case 'paid':
      return l10n.salesHistoryStatusPaid;
    case 'partiallyPaid':
      return l10n.salesHistoryStatusPartiallyPaid;
    case 'refunded':
      return l10n.salesHistoryStatusRefunded;
    case 'returned':
      return l10n.salesHistoryStatusReturned;
    default:
      return l10n.salesHistoryStatusUnknown;
  }
}

Color saleStatusColor(ThemeData theme, String status) {
  switch (status) {
    case 'paid':
      return const Color(0xFF15803D);
    case 'partiallyPaid':
      return const Color(0xFFB45309);
    case 'refunded':
    case 'returned':
      return theme.colorScheme.error;
    default:
      return theme.colorScheme.onSurfaceVariant;
  }
}

Color saleStatusBackground(ThemeData theme, String status) {
  return saleStatusColor(theme, status).withValues(alpha: 0.12);
}

Color salePaymentMethodColor(ThemeData theme, int paymentMethod) {
  switch (paymentMethod) {
    case 1:
      return const Color(0xFF15803D);
    case 2:
      return const Color(0xFF0369A1);
    case 3:
      return const Color(0xFFB45309);
    case 4:
      return theme.colorScheme.error;
    default:
      return theme.colorScheme.onSurfaceVariant;
  }
}
