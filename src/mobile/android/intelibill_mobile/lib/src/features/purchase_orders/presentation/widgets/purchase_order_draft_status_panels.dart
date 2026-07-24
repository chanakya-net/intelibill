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
    return _DraftStatusPanel(
      panelKey: bannerKey,
      icon: Icons.restore_outlined,
      title: l10n.purchaseOrderDraftRecoveredTitle,
      message: l10n.purchaseOrderDraftRecoveredMessage,
      backgroundColor: const Color(0xFFFFEDD5),
      foregroundColor: const Color(0xFF7C2D12),
      actions: [
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
    final colors = Theme.of(context).colorScheme;
    return _DraftStatusPanel(
      panelKey: bannerKey,
      icon: Icons.warning_amber_rounded,
      title: l10n.purchaseOrderDraftStorageWarning,
      message: message,
      backgroundColor: colors.errorContainer,
      foregroundColor: colors.onErrorContainer,
      iconColor: colors.error,
      actions: [
        TextButton(
          key: retryKey,
          onPressed: onRetry,
          child: Text(l10n.purchaseOrderBuilderRetry),
        ),
      ],
    );
  }
}

class _DraftStatusPanel extends StatelessWidget {
  const _DraftStatusPanel({
    required this.panelKey,
    required this.icon,
    required this.title,
    required this.message,
    required this.backgroundColor,
    required this.foregroundColor,
    required this.actions,
    this.iconColor,
  });

  final Key panelKey;
  final IconData icon;
  final String title;
  final String message;
  final Color backgroundColor;
  final Color foregroundColor;
  final Color? iconColor;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Semantics(
      liveRegion: true,
      child: Container(
        key: panelKey,
        width: double.infinity,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: backgroundColor,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: foregroundColor.withValues(alpha: 0.2)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(icon, color: iconColor ?? foregroundColor, size: 22),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    title,
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: foregroundColor,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              message,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: foregroundColor,
              ),
            ),
            if (actions.isNotEmpty) ...[
              const SizedBox(height: 12),
              Wrap(spacing: 8, runSpacing: 8, children: actions),
            ],
          ],
        ),
      ),
    );
  }
}
