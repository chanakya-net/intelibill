import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';

class CreateBankAccountSheet extends ConsumerWidget {
  const CreateBankAccountSheet({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(bankAccountsControllerProvider);
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          20,
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
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 16),
              if (state.submitFailure != null) ...[
                _SubmitFailureMessage(failure: state.submitFailure!),
                const SizedBox(height: 12),
              ],
              BankAccountForm(
                isSubmitting: state.isSubmitting,
                onSubmit: (request) => _submit(context, ref, request),
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

class _SubmitFailureMessage extends StatelessWidget {
  const _SubmitFailureMessage({required this.failure});

  final Failure failure;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final message = failure.when(
      validation: (_, _) => l10n.bankAccountsSubmitError,
      unauthorized: (_) => l10n.bankAccountsErrorUnauthorized,
      forbidden: (_) => l10n.bankAccountsErrorForbidden,
      notFound: (_) => l10n.bankAccountsSubmitError,
      server: (_, _) => l10n.bankAccountsSubmitError,
      network: (_) => l10n.bankAccountsErrorNetwork,
      timeout: (_) => l10n.bankAccountsErrorTimeout,
      serialization: (_) => l10n.bankAccountsSubmitError,
      unknown: (_) => l10n.bankAccountsSubmitError,
    );
    return Text(
      message,
      style: TextStyle(color: Theme.of(context).colorScheme.error),
    );
  }
}
