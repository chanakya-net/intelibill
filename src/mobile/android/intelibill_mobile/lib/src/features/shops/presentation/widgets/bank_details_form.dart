import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

class BankDetailsFormData {
  const BankDetailsFormData({
    this.bankName = '',
    this.accountNumber = '',
    this.accountType,
    this.ifscCode = '',
    this.accountHolderName = '',
  });

  final String bankName;
  final String accountNumber;
  final String? accountType;
  final String ifscCode;
  final String accountHolderName;

  BankDetailsFormData copyWith({
    String? bankName,
    String? accountNumber,
    String? accountType,
    String? ifscCode,
    String? accountHolderName,
    bool clearAccountType = false,
  }) {
    return BankDetailsFormData(
      bankName: bankName ?? this.bankName,
      accountNumber: accountNumber ?? this.accountNumber,
      accountType: clearAccountType ? null : (accountType ?? this.accountType),
      ifscCode: ifscCode ?? this.ifscCode,
      accountHolderName: accountHolderName ?? this.accountHolderName,
    );
  }
}

class BankDetailsForm extends StatefulWidget {
  const BankDetailsForm({
    required this.formKey,
    this.isSubmitting = false,
    this.onChanged,
    super.key,
  });

  static const bankNameFieldKey = Key('bank-details-bank-name');
  static const accountNumberFieldKey = Key('bank-details-account-number');
  static const accountTypeFieldKey = Key('bank-details-account-type');
  static const ifscCodeFieldKey = Key('bank-details-ifsc-code');
  static const accountHolderNameFieldKey = Key('bank-details-account-holder');

  static const accountTypeSavings = 'Savings';
  static const accountTypeCurrent = 'Current';
  static const accountTypeOverdraft = 'Overdraft';

  final GlobalKey<FormState> formKey;
  final bool isSubmitting;
  final ValueChanged<BankDetailsFormData>? onChanged;

  @override
  State<BankDetailsForm> createState() => _BankDetailsFormState();
}

class _BankDetailsFormState extends State<BankDetailsForm> {
  static final RegExp _ifscPattern = RegExp(r'^[A-Z]{4}0[A-Z0-9]{6}$');

  final _bankNameController = TextEditingController();
  final _accountNumberController = TextEditingController();
  final _ifscCodeController = TextEditingController();
  final _accountHolderController = TextEditingController();
  String? _selectedAccountType;

  @override
  void dispose() {
    _bankNameController.dispose();
    _accountNumberController.dispose();
    _ifscCodeController.dispose();
    _accountHolderController.dispose();
    super.dispose();
  }

  BankDetailsFormData _buildData() {
    return BankDetailsFormData(
      bankName: _bankNameController.text,
      accountNumber: _accountNumberController.text,
      accountType: _selectedAccountType,
      ifscCode: _ifscCodeController.text,
      accountHolderName: _accountHolderController.text,
    );
  }

  void _notifyChanged() {
    widget.onChanged?.call(_buildData());
  }

  String? _validateRequired(
    String? value,
    AppLocalizations l10n, {
    required String requiredMessage,
  }) {
    if ((value ?? '').trim().isEmpty) {
      return requiredMessage;
    }
    return null;
  }

  String? _validateIfsc(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return l10n.shopsCreateIfscCodeInvalid;
    }
    if (!_ifscPattern.hasMatch(trimmed)) {
      return l10n.shopsCreateIfscCodeInvalid;
    }
    return null;
  }

  List<MapEntry<String, String>> _accountTypeOptions(AppLocalizations l10n) {
    return [
      MapEntry(
        BankDetailsForm.accountTypeSavings,
        l10n.shopsCreateAccountTypeSavings,
      ),
      MapEntry(
        BankDetailsForm.accountTypeCurrent,
        l10n.shopsCreateAccountTypeCurrent,
      ),
      MapEntry(
        BankDetailsForm.accountTypeOverdraft,
        l10n.shopsCreateAccountTypeOverdraft,
      ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;

    return Form(
      key: widget.formKey,
      autovalidateMode: AutovalidateMode.onUserInteraction,
      child: Column(
        children: [
          TextFormField(
            key: BankDetailsForm.bankNameFieldKey,
            controller: _bankNameController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateBankNameLabel,
              hintText: l10n.shopsCreateBankNameHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateBankNameRequired,
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankDetailsForm.accountNumberFieldKey,
            controller: _accountNumberController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            keyboardType: TextInputType.number,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateAccountNumberLabel,
              hintText: l10n.shopsCreateAccountNumberHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateAccountNumberRequired,
            ),
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            key: BankDetailsForm.accountTypeFieldKey,
            initialValue: _selectedAccountType,
            onChanged: widget.isSubmitting
                ? null
                : (value) {
                    setState(() {
                      _selectedAccountType = value;
                    });
                    _notifyChanged();
                  },
            decoration: InputDecoration(
              labelText: l10n.shopsCreateAccountTypeLabel,
            ),
            items: _accountTypeOptions(l10n)
                .map(
                  (entry) => DropdownMenuItem<String>(
                    value: entry.key,
                    child: Text(entry.value),
                  ),
                )
                .toList(),
            validator: (value) =>
                value == null ? l10n.shopsCreateAccountTypeRequired : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankDetailsForm.ifscCodeFieldKey,
            controller: _ifscCodeController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateIfscCodeLabel,
              hintText: l10n.shopsCreateIfscCodeHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateIfsc(value, l10n),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: BankDetailsForm.accountHolderNameFieldKey,
            controller: _accountHolderController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.done,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateAccountHolderNameLabel,
              hintText: l10n.shopsCreateAccountHolderNameHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateAccountHolderNameRequired,
            ),
          ),
        ],
      ),
    );
  }
}
