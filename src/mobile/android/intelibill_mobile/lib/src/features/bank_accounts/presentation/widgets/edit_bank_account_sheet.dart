import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_sheet_message.dart';

class EditBankAccountSheet extends ConsumerWidget {
  const EditBankAccountSheet({required this.account, super.key});

  final BankAccount account;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(bankAccountsControllerProvider);
    final theme = Theme.of(context);
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          8,
          20,
          20 + MediaQuery.of(context).viewInsets.bottom,
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                l10n.bankAccountsEdit,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 16),
              _FullAccountNumber(accountNumber: account.accountNumber),
              const SizedBox(height: 12),
              if (state.submitFailure != null)
                BankAccountSubmitFailureMessage(failure: state.submitFailure!),
              BankAccountForm(
                initial: account.accountType != null
                    ? SaveBankAccountRequest(
                        bankName: account.bankName,
                        accountNumber: account.accountNumber,
                        accountType: account.accountType!,
                        ifscCode: account.ifscCode,
                        accountHolderName: account.accountHolderName,
                      )
                    : null,
                submitButtonLabel: (_) => l10n.bankAccountsUpdate,
                isSubmitting: state.isSubmitting,
                onSubmit: (request) => _submit(context, ref, request),
                onCancel: () => Navigator.of(context).pop(false),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _submit(
    BuildContext context,
    WidgetRef ref,
    SaveBankAccountRequest request,
  ) async {
    final success = await ref
        .read(bankAccountsControllerProvider.notifier)
        .updateBankAccount(account.id, request);
    if (context.mounted && success) Navigator.of(context).pop(true);
  }
}

class _FullAccountNumber extends StatelessWidget {
  const _FullAccountNumber({required this.accountNumber});

  final String accountNumber;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: theme.dividerColor),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.bankAccountsAccountNumber,
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            accountNumber,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              letterSpacing: 0.5,
            ),
          ),
        ],
      ),
    );
  }
}
