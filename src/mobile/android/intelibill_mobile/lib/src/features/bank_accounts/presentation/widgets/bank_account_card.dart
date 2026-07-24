import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';

class BankAccountCard extends StatelessWidget {
  const BankAccountCard({
    required this.account,
    this.onEdit,
    this.onDelete,
    super.key,
  });

  static const editActionKey = Key('bank-account-edit-action');
  static const deleteActionKey = Key('bank-account-delete-action');

  final BankAccount account;
  final VoidCallback? onEdit;
  final VoidCallback? onDelete;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(
              backgroundColor: theme.colorScheme.primaryContainer,
              foregroundColor: theme.colorScheme.onPrimaryContainer,
              child: const Icon(Icons.account_balance_outlined, size: 22),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          account.bankName,
                          style: theme.textTheme.titleMedium,
                        ),
                      ),
                      if (_hasValue(account.accountType))
                        _AccountTypeChip(label: account.accountType!),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    maskAccountNumber(account.accountNumber),
                    style: theme.textTheme.bodyMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                      letterSpacing: 0.5,
                    ),
                  ),
                  if (_hasValue(account.ifscCode))
                    _AccountDetail(
                      icon: Icons.tag_outlined,
                      label: l10n.bankAccountsIfsc,
                      value: account.ifscCode!,
                    ),
                  if (_hasValue(account.accountHolderName))
                    _AccountDetail(
                      icon: Icons.person_outline,
                      label: l10n.bankAccountsHolder,
                      value: account.accountHolderName!,
                    ),
                ],
              ),
            ),
            if (onEdit != null)
              IconButton(
                key: editActionKey,
                tooltip: l10n.commonEdit,
                icon: const Icon(Icons.edit_outlined),
                onPressed: onEdit,
              ),
            if (onDelete != null)
              IconButton(
                key: deleteActionKey,
                tooltip: l10n.bankAccountsDelete,
                icon: const Icon(Icons.delete_outline),
                onPressed: onDelete,
              ),
          ],
        ),
      ),
    );
  }

  static bool _hasValue(String? value) => value?.trim().isNotEmpty == true;
}

String maskAccountNumber(String accountNumber) {
  if (accountNumber.length <= 4) return accountNumber;
  return '${'*' * (accountNumber.length - 4)}${accountNumber.substring(accountNumber.length - 4)}';
}

class _AccountTypeChip extends StatelessWidget {
  const _AccountTypeChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(label, style: theme.textTheme.labelSmall),
    );
  }
}

class _AccountDetail extends StatelessWidget {
  const _AccountDetail({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Row(
        children: [
          Icon(icon, size: 16, color: theme.colorScheme.onSurfaceVariant),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              '$label: $value',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
