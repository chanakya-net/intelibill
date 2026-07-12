import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';

class BankAccountCard extends StatelessWidget {
  const BankAccountCard({
    required this.account,
    this.onEdit,
    super.key,
  });

  static const editActionKey = Key('bank-account-edit-action');

  final BankAccount account;
  final VoidCallback? onEdit;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    account.bankName,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                if (onEdit != null)
                  IconButton(
                    key: editActionKey,
                    icon: const Icon(Icons.edit),
                    onPressed: onEdit,
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(maskAccountNumber(account.accountNumber)),
            if (_hasValue(account.accountType))
              _AccountDetail(
                label: l10n.bankAccountsType,
                value: account.accountType!,
              ),
            if (_hasValue(account.ifscCode))
              _AccountDetail(
                label: l10n.bankAccountsIfsc,
                value: account.ifscCode!,
              ),
            if (_hasValue(account.accountHolderName))
              _AccountDetail(
                label: l10n.bankAccountsHolder,
                value: account.accountHolderName!,
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

class _AccountDetail extends StatelessWidget {
  const _AccountDetail({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Text('$label: $value'),
    );
  }
}
