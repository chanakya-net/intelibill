import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

class PurchaseOrderRecoveredDraftBanner extends StatelessWidget {
  const PurchaseOrderRecoveredDraftBanner({
    required this.bannerKey,
    required this.continueKey,
    required this.discardKey,
    required this.isEdit,
    required this.isBusy,
    required this.onContinue,
    required this.onDiscard,
    super.key,
  });

  final Key bannerKey;
  final Key continueKey;
  final Key discardKey;
  final bool isEdit;
  final bool isBusy;
  final VoidCallback onContinue;
  final VoidCallback onDiscard;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final colors = Theme.of(context).colorScheme;
    return Card(
      key: bannerKey,
      color: colors.secondaryContainer,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.purchaseOrderDraftRecoveredTitle,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            Text(l10n.purchaseOrderDraftRecoveredMessage),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              children: [
                FilledButton.tonal(
                  key: continueKey,
                  onPressed: isBusy ? null : onContinue,
                  child: Text(l10n.purchaseOrderDraftContinue),
                ),
                TextButton(
                  key: discardKey,
                  onPressed: isBusy ? null : onDiscard,
                  child: Text(
                    isEdit
                        ? l10n.purchaseOrderDraftDiscardAndReload
                        : l10n.purchaseOrderDraftDiscardLocal,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class PurchaseOrderStorageWarningBanner extends StatelessWidget {
  const PurchaseOrderStorageWarningBanner({
    required this.bannerKey,
    required this.retryKey,
    required this.message,
    required this.onRetry,
    super.key,
  });

  final Key bannerKey;
  final Key retryKey;
  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      key: bannerKey,
      child: ListTile(
        leading: const Icon(Icons.warning_amber_rounded),
        title: Text(l10n.purchaseOrderDraftStorageWarning),
        subtitle: Text(message),
        trailing: TextButton(
          key: retryKey,
          onPressed: onRetry,
          child: Text(l10n.purchaseOrderBuilderRetry),
        ),
      ),
    );
  }
}
