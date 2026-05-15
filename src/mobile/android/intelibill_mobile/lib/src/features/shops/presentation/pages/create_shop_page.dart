import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/controllers/shop_controller.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/bank_details_form.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_step_indicator.dart';

class CreateShopPage extends ConsumerStatefulWidget {
  const CreateShopPage({super.key});

  static const stepIndicatorKey = Key('create-shop-step-indicator');
  static const nextButtonKey = Key('create-shop-next');
  static const skipButtonKey = Key('create-shop-skip');
  static const doneButtonKey = Key('create-shop-done');

  @override
  ConsumerState<CreateShopPage> createState() => _CreateShopPageState();
}

class _CreateShopPageState extends ConsumerState<CreateShopPage> {
  final _shopInfoFormKey = GlobalKey<FormState>();
  final _bankDetailsFormKey = GlobalKey<FormState>();

  int _currentStep = 1;
  ShopInfoFormData _shopInfo = const ShopInfoFormData();
  BankDetailsFormData _bankInfo = const BankDetailsFormData();
  String? _shopName;

  bool get _isLoading => ref.watch(shopControllerProvider).isLoading;

  Future<void> _handleShopInfoNext() async {
    if (!(_shopInfoFormKey.currentState?.validate() ?? false)) {
      return;
    }

    await ref
        .read(shopControllerProvider.notifier)
        .createShop(
          CreateShopRequest(
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
    final state = ref.read(shopControllerProvider);
    if (state.hasError || state.isLoading) {
      return;
    }

    _shopName = _shopInfo.shopName.trim();
    setState(() {
      _currentStep = 2;
    });
  }

  Future<void> _handleBankDetailsNext() async {
    if (!(_bankDetailsFormKey.currentState?.validate() ?? false)) {
      return;
    }

    await ref
        .read(shopControllerProvider.notifier)
        .addBankAccount(
          '',
          AddBankAccountRequest(
            bankName: _bankInfo.bankName.trim(),
            accountNumber: _bankInfo.accountNumber.trim(),
            accountType: _bankInfo.accountType ?? '',
            ifscCode: _bankInfo.ifscCode.trim(),
            accountHolderName: _bankInfo.accountHolderName.trim(),
          ),
        );

    if (!mounted) return;
    final state = ref.read(shopControllerProvider);
    if (state.hasError || state.isLoading) {
      return;
    }

    setState(() {
      _currentStep = 3;
    });
  }

  void _handleSkip() {
    setState(() {
      _currentStep = 3;
    });
  }

  void _handleDone() {
    Navigator.of(context).pop();
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
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(message)),
        );
      }
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(
          switch (_currentStep) {
            1 => l10n.shopsCreateShopInfoStepTitle,
            2 => l10n.shopsCreateBankDetailsStepTitle,
            _ => l10n.shopsCreateSuccessTitle,
          },
        ),
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              ShopStepIndicator(
                key: CreateShopPage.stepIndicatorKey,
                currentStep: _currentStep,
              ),
              const SizedBox(height: 16),
              Expanded(
                child: _buildStepBody(l10n),
              ),
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
        return SingleChildScrollView(
          child: ShopInfoForm(
            formKey: _shopInfoFormKey,
            isSubmitting: _isLoading,
            onChanged: (data) {
              setState(() {
                _shopInfo = data;
              });
            },
          ),
        );
      case 2:
        return SingleChildScrollView(
          child: BankDetailsForm(
            formKey: _bankDetailsFormKey,
            isSubmitting: _isLoading,
            onChanged: (data) {
              setState(() {
                _bankInfo = data;
              });
            },
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
                l10n.shopsCreateSuccessTitle,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              Text(
                l10n.shopsCreateSuccessMessage(_shopName ?? 'New Shop'),
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
          key: CreateShopPage.nextButtonKey,
          onPressed: _isLoading ? null : _handleShopInfoNext,
          child: _isLoading
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.shopsCreateNextButton),
        );
      case 2:
        return Row(
          children: [
            TextButton(
              key: CreateShopPage.skipButtonKey,
              onPressed: _isLoading ? null : _handleSkip,
              child: Text(l10n.shopsCreateSkipButton),
            ),
            const Spacer(),
            FilledButton(
              key: CreateShopPage.nextButtonKey,
              onPressed: _isLoading ? null : _handleBankDetailsNext,
              child: _isLoading
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text(l10n.shopsCreateNextButton),
            ),
          ],
        );
      case 3:
        return FilledButton(
          key: CreateShopPage.doneButtonKey,
          onPressed: _isLoading ? null : _handleDone,
          child: Text(l10n.commonDone),
        );
      default:
        return const SizedBox.shrink();
    }
  }

  String _localizeFailure(Object? error, AppLocalizations l10n) {
    if (error is AppException) {
      return _localizeFailureObject(error.failure, l10n);
    }
    return l10n.shopsCreateErrorGeneric;
  }

  String _localizeFailureObject(Failure failure, AppLocalizations l10n) {
    return failure.when(
      validation: (message, _) => message ?? l10n.shopsCreateErrorGeneric,
      unauthorized: (_) => l10n.shopsCreateErrorUnauthorized,
      forbidden: (_) => l10n.shopsCreateErrorForbidden,
      notFound: (_) => l10n.shopsCreateErrorGeneric,
      server: (message, _) => message ?? l10n.shopsCreateErrorGeneric,
      network: (_) => l10n.shopsCreateErrorNetwork,
      timeout: (_) => l10n.shopsCreateErrorTimeout,
      serialization: (_) => l10n.shopsCreateErrorGeneric,
      unknown: (_) => l10n.shopsCreateErrorGeneric,
    );
  }
}
