import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';

class EditBankAccountSheet extends ConsumerWidget {
  const EditBankAccountSheet({required this.account, super.key});

  final BankAccount account;

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
                l10n.bankAccountsEdit,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 16),
              _FullAccountNumber(accountNumber: account.accountNumber),
              const SizedBox(height: 12),
              if (state.submitFailure != null) ...[
                _SubmitFailureMessage(failure: state.submitFailure!),
                const SizedBox(height: 12),
              ],
              EditBankAccountForm(
                account: account,
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
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.bankAccountsAccountNumber,
            style: Theme.of(context).textTheme.labelSmall,
          ),
          const SizedBox(height: 4),
          Text(accountNumber),
        ],
      ),
    );
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

class EditBankAccountForm extends StatefulWidget {
  const EditBankAccountForm({
    required this.account,
    required this.onSubmit,
    this.isSubmitting = false,
    super.key,
  });

  final BankAccount account;
  final Future<void> Function(SaveBankAccountRequest request) onSubmit;
  final bool isSubmitting;

  @override
  State<EditBankAccountForm> createState() => _EditBankAccountFormState();
}

class _EditBankAccountFormState extends State<EditBankAccountForm> {
  static final RegExp _ifscPattern = RegExp(r'^[A-Z]{4}0[A-Z0-9]{6}$');

  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _bankNameController;
  late final TextEditingController _accountNumberController;
  late final TextEditingController _ifscCodeController;
  late final TextEditingController _accountHolderNameController;
  late String? _accountType;

  @override
  void initState() {
    super.initState();
    _bankNameController = TextEditingController(text: widget.account.bankName);
    _accountNumberController = TextEditingController(
      text: widget.account.accountNumber,
    );
    _ifscCodeController = TextEditingController(
      text: widget.account.ifscCode ?? '',
    );
    _accountHolderNameController = TextEditingController(
      text: widget.account.accountHolderName ?? '',
    );
    _accountType = widget.account.accountType;
  }

  @override
  void dispose() {
    _bankNameController.dispose();
    _accountNumberController.dispose();
    _ifscCodeController.dispose();
    _accountHolderNameController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    await widget.onSubmit(_request());
  }

  SaveBankAccountRequest _request() {
    return SaveBankAccountRequest(
      bankName: _bankNameController.text.trim(),
      accountNumber: _accountNumberController.text.trim(),
      accountType: _accountType!,
      ifscCode: _optional(_ifscCodeController.text)?.toUpperCase(),
      accountHolderName: _optional(_accountHolderNameController.text),
    );
  }

  String? _optional(String value) {
    final trimmed = value.trim();
    return trimmed.isEmpty ? null : trimmed;
  }

  String? _required(String? value, String message, int maxLength) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) return message;
    return trimmed.length > maxLength ? message : null;
  }

  String? _optionalText(String? value, String message, int maxLength) {
    final trimmed = (value ?? '').trim();
    return trimmed.isNotEmpty && trimmed.length > maxLength ? message : null;
  }

  String? _validateIfsc(String? value, AppLocalizations l10n) {
    final ifsc = _optional(value ?? '');
    if (ifsc == null || _ifscPattern.hasMatch(ifsc.toUpperCase())) return null;
    return l10n.bankAccountsIfscInvalid;
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final enabled = !widget.isSubmitting;
    return Form(
      key: _formKey,
      autovalidateMode: AutovalidateMode.onUserInteraction,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            key: BankAccountForm.bankNameFieldKey,
            controller: _bankNameController,
            enabled: enabled,
            maxLength: 120,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(labelText: l10n.bankAccountsBankName),
            validator: (value) => _required(
              value,
              l10n.bankAccountsBankNameRequired,
              120,
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankAccountForm.accountNumberFieldKey,
            controller: _accountNumberController,
            enabled: enabled,
            maxLength: 50,
            keyboardType: TextInputType.number,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.bankAccountsAccountNumber,
            ),
            validator: (value) => _required(
              value,
              l10n.bankAccountsAccountNumberRequired,
              50,
            ),
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            key: BankAccountForm.accountTypeFieldKey,
            initialValue: _accountType,
            onChanged: enabled
                ? (value) => setState(() => _accountType = value)
                : null,
            decoration: InputDecoration(
              labelText: l10n.bankAccountsAccountType,
            ),
            items: [
              DropdownMenuItem(
                value: 'Savings',
                child: Text(l10n.bankAccountsTypeSavings),
              ),
              DropdownMenuItem(
                value: 'Current',
                child: Text(l10n.bankAccountsTypeCurrent),
              ),
            ],
            validator: (value) =>
                value == null ? l10n.bankAccountsAccountTypeRequired : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankAccountForm.ifscCodeFieldKey,
            controller: _ifscCodeController,
            enabled: enabled,
            maxLength: 11,
            textCapitalization: TextCapitalization.characters,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(labelText: l10n.bankAccountsIfsc),
            validator: (value) => _validateIfsc(value, l10n),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankAccountForm.accountHolderNameFieldKey,
            controller: _accountHolderNameController,
            enabled: enabled,
            maxLength: 120,
            textInputAction: TextInputAction.done,
            decoration: InputDecoration(labelText: l10n.bankAccountsHolder),
            validator: (value) => _optionalText(
              value,
              l10n.bankAccountsHolderMax,
              120,
            ),
          ),
          const SizedBox(height: 8),
          FilledButton(
            key: BankAccountForm.submitButtonKey,
            onPressed: enabled ? _submit : null,
            child: widget.isSubmitting
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Text(l10n.bankAccountsUpdate),
          ),
        ],
      ),
    );
  }
}
