import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_sheet_message.dart';

class CreateBankAccountSheet extends ConsumerWidget {
  const CreateBankAccountSheet({super.key});

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
                l10n.bankAccountsAdd,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 16),
              if (state.submitFailure != null)
                BankAccountSubmitFailureMessage(failure: state.submitFailure!),
              BankAccountForm(
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
        .addBankAccount(request);
    if (context.mounted && success) Navigator.of(context).pop(true);
  }
}
