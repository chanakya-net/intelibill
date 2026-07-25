import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';

class BankAccountForm extends StatefulWidget {
  const BankAccountForm({
    required this.onSubmit,
    this.isSubmitting = false,
    this.initial,
    this.submitButtonLabel,
    this.onCancel,
    super.key,
  });

  static const bankNameFieldKey = Key('bank-account-bank-name');
  static const accountNumberFieldKey = Key('bank-account-account-number');
  static const accountTypeFieldKey = Key('bank-account-account-type');
  static const ifscCodeFieldKey = Key('bank-account-ifsc-code');
  static const accountHolderNameFieldKey = Key('bank-account-account-holder');
  static const submitButtonKey = Key('bank-account-submit');

  final Future<void> Function(SaveBankAccountRequest request) onSubmit;
  final bool isSubmitting;
  final SaveBankAccountRequest? initial;
  final String Function(AppLocalizations)? submitButtonLabel;
  final VoidCallback? onCancel;

  @override
  State<BankAccountForm> createState() => _BankAccountFormState();
}

class _BankAccountFormState extends State<BankAccountForm> {
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
    _bankNameController = TextEditingController(
      text: widget.initial?.bankName ?? '',
    );
    _accountNumberController = TextEditingController(
      text: widget.initial?.accountNumber ?? '',
    );
    _ifscCodeController = TextEditingController(
      text: widget.initial?.ifscCode ?? '',
    );
    _accountHolderNameController = TextEditingController(
      text: widget.initial?.accountHolderName ?? '',
    );
    _accountType = widget.initial?.accountType;
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
          Row(
            children: [
              if (widget.onCancel != null)
                TextButton(
                  onPressed: enabled ? widget.onCancel : null,
                  child: Text(l10n.commonCancel),
                ),
              const Spacer(),
              FilledButton(
                key: BankAccountForm.submitButtonKey,
                onPressed: enabled ? _submit : null,
                child: widget.isSubmitting
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: Colors.white,
                        ),
                      )
                    : Text(
                        widget.submitButtonLabel?.call(l10n) ??
                            l10n.bankAccountsAdd,
                      ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
