import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/controllers/shop_controller.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_step_indicator.dart';

class ManageShopPage extends ConsumerStatefulWidget {
  const ManageShopPage({super.key});

  static const shopSelectorKey = Key('manage-shop-selector');
  static const nextButtonKey = Key('manage-shop-next');
  static const saveButtonKey = Key('manage-shop-save');
  static const doneButtonKey = Key('manage-shop-done');
  static const stepIndicatorKey = Key('manage-shop-step-indicator');

  @override
  ConsumerState<ManageShopPage> createState() => _ManageShopPageState();
}

class _ManageShopPageState extends ConsumerState<ManageShopPage> {
  final _shopSelectorFormKey = GlobalKey<FormState>();
  final _shopInfoFormKey = GlobalKey<FormState>();

  int _currentStep = 1;
  List<UserShop> _shops = const [];
  String? _selectedShopId;

  ShopDetails? _loadedShop;
  ShopInfoFormData _shopInfo = const ShopInfoFormData();

  bool get _isLoading => ref.watch(shopControllerProvider).isLoading;

  @override
  void initState() {
    super.initState();

    unawaited(
      Future<void>.microtask(() async {
        final authState = await ref.read(authControllerProvider.future);
        if (!mounted) return;

        final shops = authState.session?.shops ?? const [];
        setState(() {
          _shops = shops;
          if (_selectedShopId == null && shops.length == 1) {
            _selectedShopId = shops.single.shopId;
          }
        });

        if (_selectedShopId != null && shops.length == 1) {
          await _handleLoadSelectedShop(skipValidation: true);
        }
      }),
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;

    ref.listen(shopControllerProvider, (previous, next) {
      if (!mounted) return;
      final hasErrorNow = next.hasError;
      final hadErrorBefore = previous?.hasError ?? false;
      if (hasErrorNow && !hadErrorBefore) {
        final message = _localizeFailure(next.error, l10n);
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(message)));
      }
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(switch (_currentStep) {
          1 => l10n.shopsManageSelectShopTitle,
          2 => l10n.shopsManageEditDetailsTitle,
          _ => l10n.shopsManageSuccessTitle,
        }),
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              ShopStepIndicator(
                key: ManageShopPage.stepIndicatorKey,
                currentStep: _currentStep,
              ),
              const SizedBox(height: 16),
              Expanded(child: _buildStepBody(l10n)),
              const SizedBox(height: 16),
              _buildActions(l10n),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildStepBody(AppLocalizations l10n) {
    switch (_currentStep) {
      case 1:
        return Form(
          key: _shopSelectorFormKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              DropdownButtonFormField<String>(
                key: ManageShopPage.shopSelectorKey,
                initialValue: _selectedShopId,
                items: _shops
                    .map(
                      (shop) => DropdownMenuItem<String>(
                        value: shop.shopId,
                        child: Text(shop.shopName),
                      ),
                    )
                    .toList(),
                onChanged: _isLoading
                    ? null
                    : (value) {
                        setState(() {
                          _selectedShopId = value;
                        });
                      },
                decoration: InputDecoration(
                  labelText: l10n.shopsManageSelectShopLabel,
                ),
                validator: (value) =>
                    value == null ? l10n.shopsManageSelectShopRequired : null,
              ),
              const SizedBox(height: 12),
              Text(
                l10n.shopsManageSelectShopHint,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        );
      case 2:
        if (_loadedShop == null) {
          return Center(child: Text(l10n.commonLoading));
        }
        return SingleChildScrollView(
          child: Column(
            children: [
              ShopInfoForm(
                formKey: _shopInfoFormKey,
                isSubmitting: _isLoading,
                initialValue: _shopInfo,
                onChanged: (data) {
                  _shopInfo = data;
                },
              ),
            ],
          ),
        );
      case 3:
        return Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.check_circle, size: 48, color: Colors.green),
              const SizedBox(height: 12),
              Text(
                l10n.shopsManageSuccessTitle,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              Text(
                l10n.shopsManageSuccessMessage(
                  _loadedShop?.name ?? l10n.shopsManageSuccessDefaultShopName,
                ),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        );
      default:
        return const SizedBox.shrink();
    }
  }

  Widget _buildActions(AppLocalizations l10n) {
    switch (_currentStep) {
      case 1:
        return FilledButton(
          key: ManageShopPage.nextButtonKey,
          onPressed: _isLoading ? null : _handleLoadSelectedShop,
          child: _isLoading
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.shopsCreateNextButton),
        );
      case 2:
        return FilledButton(
          key: ManageShopPage.saveButtonKey,
          onPressed: _isLoading ? null : _handleSave,
          child: _isLoading
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.commonSave),
        );
      case 3:
        return FilledButton(
          key: ManageShopPage.doneButtonKey,
          onPressed: _isLoading ? null : _handleDone,
          child: Text(l10n.commonDone),
        );
      default:
        return const SizedBox.shrink();
    }
  }

  Future<void> _handleLoadSelectedShop({bool skipValidation = false}) async {
    if (!skipValidation) {
      final selectorState = _shopSelectorFormKey.currentState;
      if (selectorState != null && !selectorState.validate()) return;
    }

    final shopId = _selectedShopId;
    if (shopId == null) return;

    final details = await ref
        .read(shopControllerProvider.notifier)
        .loadShop(shopId);

    if (!mounted) return;
    final state = ref.read(shopControllerProvider);
    if (state.hasError || state.isLoading || details == null) {
      return;
    }

    final shopInfo = ShopInfoFormData(
      shopName: details.name,
      address: details.address,
      city: details.city,
      state: details.state,
      pincode: details.pincode,
      contactPerson: details.contactPerson,
      mobileNumber: details.mobileNumber,
      gstNumber: details.gstNumber,
    );

    setState(() {
      _loadedShop = details;
      _shopInfo = shopInfo;
      _currentStep = 2;
    });
  }

  Future<void> _handleSave() async {
    if (!(_shopInfoFormKey.currentState?.validate() ?? false)) {
      return;
    }

    final shopId = _loadedShop?.id;
    if (shopId == null) return;

    await ref
        .read(shopControllerProvider.notifier)
        .updateShop(
          shopId,
          UpdateShopRequest(
            name: _shopInfo.shopName.trim(),
            address: _shopInfo.address.trim(),
            city: _shopInfo.city.trim(),
            state: _shopInfo.state.trim(),
            pincode: _shopInfo.pincode.trim(),
            contactPerson: _shopInfo.contactPerson?.trim(),
            mobileNumber: _shopInfo.mobileNumber?.trim(),
            gstNumber: _shopInfo.gstNumber?.trim(),
          ),
        );

    if (!mounted) return;
    final updateState = ref.read(shopControllerProvider);
    if (updateState.hasError || updateState.isLoading) {
      return;
    }

    setState(() {
      _currentStep = 3;
    });
  }

  void _handleDone() {
    Navigator.of(context).pop();
  }

  String _localizeFailure(Object? error, AppLocalizations l10n) {
    if (error is AppException) {
      return _localizeFailureObject(error.failure, l10n);
    }
    return l10n.shopsManageErrorGeneric;
  }

  String _localizeFailureObject(Failure failure, AppLocalizations l10n) {
    return failure.when(
      validation: (message, _) => message ?? l10n.shopsManageErrorGeneric,
      unauthorized: (_) => l10n.shopsManageErrorUnauthorized,
      forbidden: (_) => l10n.shopsManageErrorForbidden,
      notFound: (_) => l10n.shopsManageErrorGeneric,
      server: (message, _) => message ?? l10n.shopsManageErrorGeneric,
      network: (_) => l10n.shopsManageErrorNetwork,
      timeout: (_) => l10n.shopsManageErrorTimeout,
      serialization: (_) => l10n.shopsManageErrorGeneric,
      unknown: (_) => l10n.shopsManageErrorGeneric,
    );
  }
}
