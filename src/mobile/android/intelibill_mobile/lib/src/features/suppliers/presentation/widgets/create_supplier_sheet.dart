import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';

class CreateSupplierSheet extends ConsumerStatefulWidget {
  const CreateSupplierSheet({super.key});

  static const nameFieldKey = Key('create-supplier-name');
  static const contactPersonFieldKey = Key('create-supplier-contact-person');
  static const contactPhoneFieldKey = Key('create-supplier-contact-phone');
  static const addressFieldKey = Key('create-supplier-address');
  static const cityFieldKey = Key('create-supplier-city');
  static const stateFieldKey = Key('create-supplier-state');
  static const pinFieldKey = Key('create-supplier-pin');
  static const activeSwitchKey = Key('create-supplier-active');
  static const preferredSwitchKey = Key('create-supplier-preferred');
  static const submitButtonKey = Key('create-supplier-submit');
  static const cancelButtonKey = Key('create-supplier-cancel');

  @override
  ConsumerState<CreateSupplierSheet> createState() =>
      _CreateSupplierSheetState();
}

class _CreateSupplierSheetState extends ConsumerState<CreateSupplierSheet> {
  static final RegExp _phonePattern = RegExp(r'^\+?[0-9]{7,15}$');

  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _contactPersonController = TextEditingController();
  final _contactPhoneController = TextEditingController();
  final _addressController = TextEditingController();
  final _cityController = TextEditingController();
  final _stateController = TextEditingController();
  final _pinController = TextEditingController();

  bool _isActive = true;
  bool _isPreferred = false;

  @override
  void dispose() {
    _nameController.dispose();
    _contactPersonController.dispose();
    _contactPhoneController.dispose();
    _addressController.dispose();
    _cityController.dispose();
    _stateController.dispose();
    _pinController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    String? optionalValue(TextEditingController controller) {
      final trimmed = controller.text.trim();
      return trimmed.isEmpty ? null : trimmed;
    }

    final success = await ref
        .read(suppliersControllerProvider.notifier)
        .createSupplier(
          name: _nameController.text.trim(),
          contactPersonName: optionalValue(_contactPersonController),
          contactPersonPhone: optionalValue(_contactPhoneController),
          address: _addressController.text.trim(),
          city: _cityController.text.trim(),
          state: _stateController.text.trim(),
          pin: _pinController.text.trim(),
          isActive: _isActive,
          isPreferred: _isPreferred,
        );

    if (!mounted) return;
    if (success) {
      Navigator.of(context).pop(true);
    }
  }

  String? _validateRequired(
    String? value, {
    required String requiredMessage,
    required String maxMessage,
    required int maxLength,
  }) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return requiredMessage;
    }
    if (trimmed.length > maxLength) {
      return maxMessage;
    }
    return null;
  }

  String? _validateOptional(
    String? value, {
    required String maxMessage,
    required int maxLength,
    RegExp? pattern,
    String? patternMessage,
  }) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return null;
    }
    if (trimmed.length > maxLength) {
      return maxMessage;
    }
    if (pattern != null && !pattern.hasMatch(trimmed)) {
      return patternMessage;
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(suppliersControllerProvider);
    final theme = Theme.of(context);
    final isSubmitting = state.isSubmitting;

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 20,
          right: 20,
          top: 20,
          bottom: 20 + MediaQuery.of(context).viewInsets.bottom,
        ),
        child: Form(
          key: _formKey,
          autovalidateMode: AutovalidateMode.onUserInteraction,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  l10n.suppliersAddSupplier,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 16),
                if (state.submitFailure != null)
                  Container(
                    margin: const EdgeInsets.only(bottom: 12),
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(
                        color: theme.colorScheme.errorContainer,
                      ),
                      color: theme.colorScheme.errorContainer,
                    ),
                    child: Text(
                      _localizeCreateFailure(l10n, state.submitFailure!),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                TextFormField(
                  key: CreateSupplierSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateNameLabel,
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.suppliersCreateNameRequired,
                    maxMessage: l10n.suppliersCreateNameMax,
                    maxLength: 180,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.contactPersonFieldKey,
                  controller: _contactPersonController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateContactPersonLabel,
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.suppliersCreateContactPersonMax,
                    maxLength: 120,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.contactPhoneFieldKey,
                  controller: _contactPhoneController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.phone,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateContactPhoneLabel,
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.suppliersCreateContactPhoneMax,
                    maxLength: 32,
                    pattern: _phonePattern,
                    patternMessage: l10n.suppliersCreateContactPhoneInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.addressFieldKey,
                  controller: _addressController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateAddressLabel,
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.suppliersCreateAddressRequired,
                    maxMessage: l10n.suppliersCreateAddressMax,
                    maxLength: 320,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.cityFieldKey,
                  controller: _cityController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateCityLabel,
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.suppliersCreateCityRequired,
                    maxMessage: l10n.suppliersCreateCityMax,
                    maxLength: 120,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.stateFieldKey,
                  controller: _stateController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreateStateLabel,
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.suppliersCreateStateRequired,
                    maxMessage: l10n.suppliersCreateStateMax,
                    maxLength: 120,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateSupplierSheet.pinFieldKey,
                  controller: _pinController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.done,
                  decoration: InputDecoration(
                    labelText: l10n.suppliersCreatePinLabel,
                  ),
                  validator: (value) => _validateRequired(
                    value,
                    requiredMessage: l10n.suppliersCreatePinRequired,
                    maxMessage: l10n.suppliersCreatePinMax,
                    maxLength: 16,
                  ),
                ),
                const SizedBox(height: 4),
                SwitchListTile(
                  key: CreateSupplierSheet.activeSwitchKey,
                  value: _isActive,
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          setState(() {
                            _isActive = value;
                          });
                        },
                  title: Text(l10n.suppliersCreateActiveLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                SwitchListTile(
                  key: CreateSupplierSheet.preferredSwitchKey,
                  value: _isPreferred,
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          setState(() {
                            _isPreferred = value;
                          });
                        },
                  title: Text(l10n.suppliersCreatePreferredLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    TextButton(
                      key: CreateSupplierSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () {
                              Navigator.of(context).pop(false);
                            },
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: CreateSupplierSheet.submitButtonKey,
                      onPressed: isSubmitting ? null : _submit,
                      child: isSubmitting
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                              ),
                            )
                          : Text(l10n.suppliersAddSupplier),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

String _localizeCreateFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? errors) {
      if (message != null && message.isNotEmpty) {
        return message;
      }
      if (errors != null && errors.isNotEmpty) {
        return errors.values.first.first;
      }
      return l10n.suppliersCreateErrorGeneric;
    },
    unauthorized: (String? _) => l10n.suppliersCreateErrorUnauthorized,
    forbidden: (String? _) => l10n.suppliersCreateErrorForbidden,
    notFound: (String? _) => l10n.suppliersCreateErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.suppliersCreateErrorGeneric,
    network: (String? _) => l10n.suppliersCreateErrorNetwork,
    timeout: (String? _) => l10n.suppliersCreateErrorTimeout,
    serialization: (String? _) => l10n.suppliersCreateErrorGeneric,
    unknown: (String? _) => l10n.suppliersCreateErrorGeneric,
  );
}
