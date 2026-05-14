import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';

class CreateCustomerSheet extends ConsumerStatefulWidget {
  const CreateCustomerSheet({super.key});

  static const nameFieldKey = Key('create-customer-name');
  static const phoneFieldKey = Key('create-customer-phone');
  static const addressFieldKey = Key('create-customer-address');
  static const activeSwitchKey = Key('create-customer-active');
  static const submitButtonKey = Key('create-customer-submit');
  static const cancelButtonKey = Key('create-customer-cancel');

  @override
  ConsumerState<CreateCustomerSheet> createState() =>
      _CreateCustomerSheetState();
}

class _CreateCustomerSheetState extends ConsumerState<CreateCustomerSheet> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _phoneController = TextEditingController();
  final _addressController = TextEditingController();

  bool _isActive = true;

  @override
  void dispose() {
    _nameController.dispose();
    _phoneController.dispose();
    _addressController.dispose();
    super.dispose();
  }

  Future<void> _submit(CustomersState state) async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    final address = _addressController.text.trim();
    final success = await ref
        .read(customersControllerProvider.notifier)
        .createCustomer(
          name: _nameController.text.trim(),
          phoneNumber: _phoneController.text.trim(),
          address: address.isEmpty ? null : address,
          isActive: _isActive,
        );

    if (!mounted) return;
    if (success) {
      Navigator.of(context).pop(true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(customersControllerProvider);
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
                  l10n.customersAddCustomer,
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
                  key: CreateCustomerSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.customersCreateNameLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.customersCreateNameRequired;
                    }
                    if (trimmed.length > 180) {
                      return l10n.customersCreateNameMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateCustomerSheet.phoneFieldKey,
                  controller: _phoneController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.phone,
                  decoration: InputDecoration(
                    labelText: l10n.customersCreatePhoneLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.customersCreatePhoneRequired;
                    }
                    if (trimmed.length > 32) {
                      return l10n.customersCreatePhoneMax;
                    }
                    final phonePattern = RegExp(r'^\+?[0-9]{7,15}$');
                    if (!phonePattern.hasMatch(trimmed)) {
                      return l10n.customersCreatePhoneInvalid;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateCustomerSheet.addressFieldKey,
                  controller: _addressController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.newline,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.customersCreateAddressLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.length > 320) {
                      return l10n.customersCreateAddressMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 4),
                SwitchListTile(
                  key: CreateCustomerSheet.activeSwitchKey,
                  value: _isActive,
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          setState(() {
                            _isActive = value;
                          });
                        },
                  title: Text(l10n.customersCreateActiveLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    TextButton(
                      key: CreateCustomerSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () {
                              Navigator.of(context).pop(false);
                            },
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: CreateCustomerSheet.submitButtonKey,
                      onPressed: isSubmitting ? null : () => _submit(state),
                      child: isSubmitting
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                              ),
                            )
                          : Text(l10n.customersAddCustomer),
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
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.customersCreateErrorGeneric,
    unauthorized: (String? _) => l10n.customersCreateErrorUnauthorized,
    forbidden: (String? _) => l10n.customersCreateErrorForbidden,
    notFound: (String? _) => l10n.customersCreateErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.customersCreateErrorGeneric,
    network: (String? _) => l10n.customersCreateErrorNetwork,
    timeout: (String? _) => l10n.customersCreateErrorTimeout,
    serialization: (String? _) => l10n.customersCreateErrorGeneric,
    unknown: (String? _) => l10n.customersCreateErrorGeneric,
  );
}

extension CreateCustomerLocalizationFallback on AppLocalizations {
  String get customersCreateNameLabel => 'Name';

  String get customersCreateNameRequired => 'Name is required.';

  String get customersCreateNameMax => 'Name must be 180 characters or fewer.';

  String get customersCreatePhoneLabel => 'Phone Number';

  String get customersCreatePhoneRequired => 'Phone number is required.';

  String get customersCreatePhoneMax =>
      'Phone number must be 32 characters or fewer.';

  String get customersCreatePhoneInvalid => 'Enter a valid phone number.';

  String get customersCreateAddressLabel => 'Address';

  String get customersCreateAddressMax =>
      'Address must be 320 characters or fewer.';

  String get customersCreateActiveLabel => 'Active';

  String get customersCreateSuccess => 'Customer created successfully.';

  String get customersCreateErrorGeneric => customersErrorGeneric;

  String get customersCreateErrorUnauthorized => customersErrorUnauthorized;

  String get customersCreateErrorForbidden => customersErrorForbidden;

  String get customersCreateErrorNetwork => customersErrorNetwork;

  String get customersCreateErrorTimeout => customersErrorTimeout;
}
