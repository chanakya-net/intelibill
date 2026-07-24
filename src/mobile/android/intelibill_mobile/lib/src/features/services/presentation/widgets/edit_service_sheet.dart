import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';

class EditServiceSheet extends ConsumerStatefulWidget {
  const EditServiceSheet({required this.service, super.key});

  final Service service;

  static const nameFieldKey = Key('edit-service-name');
  static const descriptionFieldKey = Key('edit-service-description');
  static const priceFieldKey = Key('edit-service-price');
  static const hsnCodeFieldKey = Key('edit-service-hsn-code');
  static const taxRateFieldKey = Key('edit-service-tax-rate');
  static const taxIncludedSwitchKey = Key('edit-service-tax-included');
  static const submitButtonKey = Key('edit-service-submit');
  static const cancelButtonKey = Key('edit-service-cancel');

  @override
  ConsumerState<EditServiceSheet> createState() => _EditServiceSheetState();
}

class _EditServiceSheetState extends ConsumerState<EditServiceSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _descriptionController;
  late final TextEditingController _priceController;
  late final TextEditingController _hsnCodeController;
  late final TextEditingController _taxRateController;
  late bool _taxIncluded;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.service.name);
    _descriptionController = TextEditingController(
      text: widget.service.description ?? '',
    );
    _priceController = TextEditingController(
      text: widget.service.price.toStringAsFixed(2),
    );
    _hsnCodeController = TextEditingController(
      text: widget.service.hsnCode ?? '',
    );
    _taxRateController = TextEditingController(
      text: widget.service.taxRatePercent.toStringAsFixed(2),
    );
    _taxIncluded = widget.service.taxIncluded;
  }

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
        .updateService(
          serviceId: widget.service.serviceId,
          name: _nameController.text.trim(),
          description: description.isEmpty ? null : description,
          price: double.parse(_priceController.text.trim()),
          hsnCode: hsnCode.isEmpty ? null : hsnCode,
          taxRatePercent: double.parse(_taxRateController.text.trim()),
          taxIncluded: _taxIncluded,
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
                  l10n.servicesEditService,
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
                  key: EditServiceSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesNameLabel,
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
                  key: EditServiceSheet.descriptionFieldKey,
                  controller: _descriptionController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.newline,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.servicesDescriptionLabel,
                  ),
                  validator: (value) => _validateOptionalText(
                    value,
                    maxMessage: l10n.servicesDescriptionMax,
                    maxLength: 1000,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditServiceSheet.priceFieldKey,
                  controller: _priceController,
                  enabled: !isSubmitting,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesPriceLabel,
                  ),
                  validator: (value) => _validatePrice(
                    value,
                    requiredMessage: l10n.servicesPriceRequired,
                    invalidMessage: l10n.servicesPriceInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditServiceSheet.hsnCodeFieldKey,
                  controller: _hsnCodeController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.number,
                  decoration: InputDecoration(
                    labelText: l10n.servicesHsnCodeLabel,
                  ),
                  validator: (value) => _validateHsnCode(
                    value,
                    invalidMessage: l10n.servicesHsnCodeInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditServiceSheet.taxRateFieldKey,
                  controller: _taxRateController,
                  enabled: !isSubmitting,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.servicesTaxRateLabel,
                  ),
                  validator: (value) => _validateTaxRate(
                    value,
                    requiredMessage: l10n.servicesTaxRateRequired,
                    invalidMessage: l10n.servicesTaxRateInvalid,
                  ),
                ),
                const SizedBox(height: 4),
                SwitchListTile(
                  key: EditServiceSheet.taxIncludedSwitchKey,
                  value: _taxIncluded,
                  onChanged: isSubmitting
                      ? null
                      : (value) => setState(() => _taxIncluded = value),
                  title: Text(l10n.servicesTaxIncludedLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    TextButton(
                      key: EditServiceSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () => Navigator.of(context).pop(false),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: EditServiceSheet.submitButtonKey,
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
                          : Text(l10n.commonSave),
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
