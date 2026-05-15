import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_localizations.dart';

class ShopInfoFormData {
  const ShopInfoFormData({
    this.shopName = '',
    this.address = '',
    this.city = '',
    this.state = '',
    this.pincode = '',
    this.contactPerson,
    this.mobileNumber,
    this.gstNumber,
  });

  final String shopName;
  final String address;
  final String city;
  final String state;
  final String pincode;
  final String? contactPerson;
  final String? mobileNumber;
  final String? gstNumber;

  ShopInfoFormData copyWith({
    String? shopName,
    String? address,
    String? city,
    String? state,
    String? pincode,
    String? contactPerson,
    String? mobileNumber,
    String? gstNumber,
    bool clearContactPerson = false,
    bool clearMobileNumber = false,
    bool clearGstNumber = false,
  }) {
    return ShopInfoFormData(
      shopName: shopName ?? this.shopName,
      address: address ?? this.address,
      city: city ?? this.city,
      state: state ?? this.state,
      pincode: pincode ?? this.pincode,
      contactPerson: clearContactPerson
          ? null
          : (contactPerson ?? this.contactPerson),
      mobileNumber: clearMobileNumber ? null : (mobileNumber ?? this.mobileNumber),
      gstNumber: clearGstNumber ? null : (gstNumber ?? this.gstNumber),
    );
  }
}

class ShopInfoForm extends StatefulWidget {
  const ShopInfoForm({
    required this.formKey,
    this.isSubmitting = false,
    this.onChanged,
    super.key,
  });

  static const shopNameFieldKey = Key('shop-info-name');
  static const addressFieldKey = Key('shop-info-address');
  static const cityFieldKey = Key('shop-info-city');
  static const stateFieldKey = Key('shop-info-state');
  static const pincodeFieldKey = Key('shop-info-pincode');
  static const contactPersonFieldKey = Key('shop-info-contact-person');
  static const mobileNumberFieldKey = Key('shop-info-mobile-number');
  static const gstNumberFieldKey = Key('shop-info-gst-number');

  final GlobalKey<FormState> formKey;
  final bool isSubmitting;
  final ValueChanged<ShopInfoFormData>? onChanged;

  @override
  State<ShopInfoForm> createState() => _ShopInfoFormState();
}

class _ShopInfoFormState extends State<ShopInfoForm> {
  static final RegExp _pincodePattern = RegExp(r'^\d{6}$');
  static final RegExp _mobilePattern = RegExp(r'^\d{10}$');
  static final RegExp _gstPattern = RegExp(
    r'^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$',
  );

  final _shopNameController = TextEditingController();
  final _addressController = TextEditingController();
  final _cityController = TextEditingController();
  final _stateController = TextEditingController();
  final _pincodeController = TextEditingController();
  final _contactPersonController = TextEditingController();
  final _mobileController = TextEditingController();
  final _gstController = TextEditingController();

  @override
  void dispose() {
    _shopNameController.dispose();
    _addressController.dispose();
    _cityController.dispose();
    _stateController.dispose();
    _pincodeController.dispose();
    _contactPersonController.dispose();
    _mobileController.dispose();
    _gstController.dispose();
    super.dispose();
  }

  ShopInfoFormData _buildData() {
    return ShopInfoFormData(
      shopName: _shopNameController.text,
      address: _addressController.text,
      city: _cityController.text,
      state: _stateController.text,
      pincode: _pincodeController.text,
      contactPerson: _contactPersonController.text.trim().isEmpty
          ? null
          : _contactPersonController.text.trim(),
      mobileNumber: _mobileController.text.trim().isEmpty
          ? null
          : _mobileController.text.trim(),
      gstNumber: _gstController.text.trim().isEmpty
          ? null
          : _gstController.text.trim(),
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

  String? _validatePincode(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return l10n.shopsCreatePincodeRequired;
    }
    if (!_pincodePattern.hasMatch(trimmed)) {
      return l10n.shopsCreatePincodeInvalid;
    }
    return null;
  }

  String? _validateMobile(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return null;
    }
    if (!_mobilePattern.hasMatch(trimmed)) {
      return l10n.shopsCreateMobileNumberInvalid;
    }
    return null;
  }

  String? _validateGst(String? value, AppLocalizations l10n) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return null;
    }
    if (!_gstPattern.hasMatch(trimmed)) {
      return l10n.shopsCreateGstNumberInvalid;
    }
    return null;
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
            key: ShopInfoForm.shopNameFieldKey,
            controller: _shopNameController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateShopNameLabel,
              hintText: l10n.shopsCreateShopNameHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) =>
                _validateRequired(value, l10n, requiredMessage: l10n.shopsCreateShopNameRequired),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.addressFieldKey,
            controller: _addressController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            maxLines: 3,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateAddressLabel,
              hintText: l10n.shopsCreateAddressHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateAddressRequired,
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.cityFieldKey,
            controller: _cityController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateCityLabel,
              hintText: l10n.shopsCreateCityHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateCityRequired,
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.stateFieldKey,
            controller: _stateController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateStateLabel,
              hintText: l10n.shopsCreateStateHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateRequired(
              value,
              l10n,
              requiredMessage: l10n.shopsCreateStateRequired,
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.pincodeFieldKey,
            controller: _pincodeController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            keyboardType: TextInputType.number,
            decoration: InputDecoration(
              labelText: l10n.shopsCreatePincodeLabel,
              hintText: l10n.shopsCreatePincodeHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validatePincode(value, l10n),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.contactPersonFieldKey,
            controller: _contactPersonController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateContactPersonLabel,
              hintText: l10n.shopsCreateContactPersonHint,
            ),
            onChanged: (_) => _notifyChanged(),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.mobileNumberFieldKey,
            controller: _mobileController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.next,
            keyboardType: TextInputType.phone,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateMobileNumberLabel,
              hintText: l10n.shopsCreateMobileNumberHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateMobile(value, l10n),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ShopInfoForm.gstNumberFieldKey,
            controller: _gstController,
            enabled: !widget.isSubmitting,
            textInputAction: TextInputAction.done,
            decoration: InputDecoration(
              labelText: l10n.shopsCreateGstNumberLabel,
              hintText: l10n.shopsCreateGstNumberHint,
            ),
            onChanged: (_) => _notifyChanged(),
            validator: (value) => _validateGst(value, l10n),
          ),
        ],
      ),
    );
  }
}
