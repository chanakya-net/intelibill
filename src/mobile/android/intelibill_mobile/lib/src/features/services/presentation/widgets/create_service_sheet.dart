import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';

class CreateServiceSheet extends ConsumerStatefulWidget {
  const CreateServiceSheet({super.key});

  static const nameFieldKey = Key('create-service-name');
  static const descriptionFieldKey = Key('create-service-description');
  static const priceFieldKey = Key('create-service-price');
  static const hsnCodeFieldKey = Key('create-service-hsn-code');
  static const taxRateFieldKey = Key('create-service-tax-rate');
  static const taxIncludedSwitchKey = Key('create-service-tax-included');
  static const activeSwitchKey = Key('create-service-active');
  static const submitButtonKey = Key('create-service-submit');
  static const cancelButtonKey = Key('create-service-cancel');

  @override
  ConsumerState<CreateServiceSheet> createState() => _CreateServiceSheetState();
}

class _CreateServiceSheetState extends ConsumerState<CreateServiceSheet> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _descriptionController = TextEditingController();
  final _priceController = TextEditingController();
  final _hsnCodeController = TextEditingController();
  final _taxRateController = TextEditingController(text: '0');

  bool _taxIncluded = true;
  bool _isActive = true;

  @override
  void dispose() {
    _nameController.dispose();
    _descriptionController.dispose();
    _priceController.dispose();
    _hsnCodeController.dispose();
    _taxRateController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    final description = _descriptionController.text.trim();
    final hsnCode = _hsnCodeController.text.trim();
    final success = await ref
        .read(servicesControllerProvider.notifier)
        .createService(
          name: _nameController.text.trim(),
          description: description.isEmpty ? null : description,
          price: double.parse(_priceController.text.trim()),
          hsnCode: hsnCode.isEmpty ? null : hsnCode,
          taxRatePercent: double.parse(_taxRateController.text.trim()),
          taxIncluded: _taxIncluded,
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
    final state = ref.watch(servicesControllerProvider);
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
                  l10n.servicesAddService,
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
                      _localizeSubmitFailure(l10n, state.submitFailure!),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                TextFormField(
                  key: CreateServiceSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesNameLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateRequiredText(
                    value,
                    requiredMessage: l10n.servicesNameRequired,
                    maxMessage: l10n.servicesNameMax,
                    maxLength: 180,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateServiceSheet.descriptionFieldKey,
                  controller: _descriptionController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.newline,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.servicesDescriptionLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateOptionalText(
                    value,
                    maxMessage: l10n.servicesDescriptionMax,
                    maxLength: 1000,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateServiceSheet.priceFieldKey,
                  controller: _priceController,
                  enabled: !isSubmitting,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesPriceLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validatePrice(
                    value,
                    requiredMessage: l10n.servicesPriceRequired,
                    invalidMessage: l10n.servicesPriceInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateServiceSheet.hsnCodeFieldKey,
                  controller: _hsnCodeController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.number,
                  decoration: InputDecoration(
                    labelText: l10n.servicesHsnCodeLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateHsnCode(
                    value,
                    invalidMessage: l10n.servicesHsnCodeInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateServiceSheet.taxRateFieldKey,
                  controller: _taxRateController,
                  enabled: !isSubmitting,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesTaxRateLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateTaxRate(
                    value,
                    requiredMessage: l10n.servicesTaxRateRequired,
                    invalidMessage: l10n.servicesTaxRateInvalid,
                  ),
                ),
                const SizedBox(height: 4),
                SwitchListTile(
                  key: CreateServiceSheet.taxIncludedSwitchKey,
                  value: _taxIncluded,
                  onChanged: isSubmitting
                      ? null
                      : (value) => setState(() => _taxIncluded = value),
                  title: Text(l10n.servicesTaxIncludedLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                SwitchListTile(
                  key: CreateServiceSheet.activeSwitchKey,
                  value: _isActive,
                  onChanged: isSubmitting
                      ? null
                      : (value) => setState(() => _isActive = value),
                  title: Text(l10n.servicesActiveOnCreateLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    TextButton(
                      key: CreateServiceSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () => Navigator.of(context).pop(false),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: CreateServiceSheet.submitButtonKey,
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
                          : Text(l10n.servicesAddService),
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

String? _validateRequiredText(
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

String? _validateOptionalText(
  String? value, {
  required String maxMessage,
  required int maxLength,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) {
    return null;
  }
  if (trimmed.length > maxLength) {
    return maxMessage;
  }
  return null;
}

String? _validatePrice(
  String? value, {
  required String requiredMessage,
  required String invalidMessage,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) {
    return requiredMessage;
  }
  final parsed = double.tryParse(trimmed);
  if (parsed == null || parsed <= 0) {
    return invalidMessage;
  }
  return null;
}

String? _validateHsnCode(
  String? value, {
  required String invalidMessage,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) {
    return null;
  }
  final pattern = RegExp(r'^\d{4,8}$');
  if (!pattern.hasMatch(trimmed)) {
    return invalidMessage;
  }
  return null;
}

String? _validateTaxRate(
  String? value, {
  required String requiredMessage,
  required String invalidMessage,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) {
    return requiredMessage;
  }
  final parsed = double.tryParse(trimmed);
  if (parsed == null || parsed < 0 || parsed > 100) {
    return invalidMessage;
  }
  return null;
}

String _localizeSubmitFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.servicesMutationErrorGeneric,
    unauthorized: (String? _) => l10n.servicesMutationErrorUnauthorized,
    forbidden: (String? _) => l10n.servicesMutationErrorForbidden,
    notFound: (String? _) => l10n.servicesMutationErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.servicesMutationErrorGeneric,
    network: (String? _) => l10n.servicesMutationErrorNetwork,
    timeout: (String? _) => l10n.servicesMutationErrorTimeout,
    serialization: (String? _) => l10n.servicesMutationErrorGeneric,
    unknown: (String? _) => l10n.servicesMutationErrorGeneric,
  );
}
