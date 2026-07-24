import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';

class AddShopUserSheet extends ConsumerStatefulWidget {
  const AddShopUserSheet({super.key});

  static const firstNameFieldKey = Key('add-shop-user-first-name');
  static const lastNameFieldKey = Key('add-shop-user-last-name');
  static const emailFieldKey = Key('add-shop-user-email');
  static const phoneFieldKey = Key('add-shop-user-phone');
  static const passwordFieldKey = Key('add-shop-user-password');
  static const confirmPasswordFieldKey = Key('add-shop-user-confirm-password');
  static const submitButtonKey = Key('add-shop-user-submit');

  @override
  ConsumerState<AddShopUserSheet> createState() => _AddShopUserSheetState();
}

class _AddShopUserSheetState extends ConsumerState<AddShopUserSheet> {
  final _formKey = GlobalKey<FormState>();
  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _emailController = TextEditingController();
  final _phoneController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmPasswordController = TextEditingController();

  final Set<String> _selectedShopIds = {};
  String _role = 'Manager';
  bool _initializedShopSelection = false;

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _phoneController.dispose();
    _passwordController.dispose();
    _confirmPasswordController.dispose();
    super.dispose();
  }

  List<UserShop> _availableShops(AuthSession? session) {
    return session?.shops ?? const [];
  }

  void _initializeShopSelection(AuthSession? session) {
    if (_initializedShopSelection) {
      return;
    }

    final shops = _availableShops(session);
    if (shops.isEmpty) {
      return;
    }

    _initializedShopSelection = true;
    final activeShopId = session?.activeShopId;
    if (activeShopId != null && activeShopId.isNotEmpty) {
      _selectedShopIds.add(activeShopId);
      return;
    }

    for (final shop in shops) {
      if (shop.isDefault) {
        _selectedShopIds.add(shop.shopId);
        return;
      }
    }

    _selectedShopIds.add(shops.first.shopId);
  }

  Future<void> _submit(UsersState state) async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    if (_selectedShopIds.isEmpty) {
      return;
    }

    final success = await ref
        .read(usersControllerProvider.notifier)
        .addShopUser(
          shopIds: _selectedShopIds.toList(),
          email: _emailController.text.trim(),
          firstName: _firstNameController.text.trim(),
          lastName: _lastNameController.text.trim(),
          phoneNumber: _phoneController.text.trim(),
          password: _passwordController.text,
          confirmPassword: _confirmPasswordController.text,
          role: _role,
        );

    if (!mounted) return;
    if (success) {
      Navigator.of(context).pop(true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(usersControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final session = authState.value?.session;
    _initializeShopSelection(session);
    final shops = _availableShops(session);
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
                  l10n.usersAddUser,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  l10n.usersAddUserDescription,
                  style: theme.textTheme.bodyMedium,
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
                      _localizeFailure(l10n, state.submitFailure!),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                Text(
                  l10n.usersShopsLabel,
                  style: theme.textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  l10n.usersSelectShopsDescription,
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: 8),
                ...shops.map((shop) {
                  return CheckboxListTile(
                    value: _selectedShopIds.contains(shop.shopId),
                    onChanged: isSubmitting
                        ? null
                        : (checked) {
                            setState(() {
                              if (checked == true) {
                                _selectedShopIds.add(shop.shopId);
                              } else {
                                _selectedShopIds.remove(shop.shopId);
                              }
                            });
                          },
                    title: Text(shop.shopName),
                    subtitle: shop.isDefault
                        ? Text(l10n.usersDefaultShop)
                        : null,
                    contentPadding: EdgeInsets.zero,
                    controlAffinity: ListTileControlAffinity.leading,
                  );
                }),
                if (_selectedShopIds.isEmpty)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 8),
                    child: Text(
                      l10n.usersSelectAtLeastOneShop,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.error,
                      ),
                    ),
                  ),
                const SizedBox(height: 8),
                TextFormField(
                  key: AddShopUserSheet.firstNameFieldKey,
                  controller: _firstNameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.usersFirstNameLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.usersFirstNameRequired;
                    }
                    if (trimmed.length > 100) {
                      return l10n.usersFirstNameMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddShopUserSheet.lastNameFieldKey,
                  controller: _lastNameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.usersLastNameLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.usersLastNameRequired;
                    }
                    if (trimmed.length > 100) {
                      return l10n.usersLastNameMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddShopUserSheet.emailFieldKey,
                  controller: _emailController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.emailAddress,
                  decoration: InputDecoration(
                    labelText: l10n.usersEmailLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.usersEmailRequired;
                    }
                    if (trimmed.length > 256) {
                      return l10n.usersEmailMax;
                    }
                    final emailPattern = RegExp(r'^[^@]+@[^@]+\.[^@]+$');
                    if (!emailPattern.hasMatch(trimmed)) {
                      return l10n.authValidationEmailInvalid;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddShopUserSheet.phoneFieldKey,
                  controller: _phoneController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  keyboardType: TextInputType.phone,
                  decoration: InputDecoration(
                    labelText: l10n.usersPhoneLabel,
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.usersPhoneRequired;
                    }
                    if (trimmed.length > 32) {
                      return l10n.usersPhoneMax;
                    }
                    final phonePattern = RegExp(r'^\+?[0-9]{7,15}$');
                    if (!phonePattern.hasMatch(trimmed)) {
                      return l10n.usersPhoneInvalid;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddShopUserSheet.passwordFieldKey,
                  controller: _passwordController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  obscureText: true,
                  decoration: InputDecoration(
                    labelText: l10n.authPassword,
                  ),
                  validator: (value) {
                    final password = value ?? '';
                    if (password.isEmpty) {
                      return l10n.authValidationPasswordRequired;
                    }
                    if (password.length < 8) {
                      return l10n.usersPasswordMin;
                    }
                    if (password.length > 100) {
                      return l10n.usersPasswordMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: AddShopUserSheet.confirmPasswordFieldKey,
                  controller: _confirmPasswordController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.done,
                  obscureText: true,
                  decoration: InputDecoration(
                    labelText: l10n.usersConfirmPasswordLabel,
                  ),
                  validator: (value) {
                    if ((value ?? '').isEmpty) {
                      return l10n.usersConfirmPasswordRequired;
                    }
                    if (value != _passwordController.text) {
                      return l10n.usersPasswordMismatch;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: _role,
                  decoration: InputDecoration(
                    labelText: l10n.usersRoleLabel,
                  ),
                  items: [
                    DropdownMenuItem(
                      value: 'Manager',
                      child: Text(l10n.usersRoleManager),
                    ),
                    DropdownMenuItem(
                      value: 'Staff',
                      child: Text(l10n.usersRoleStaff),
                    ),
                  ],
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          if (value == null) return;
                          setState(() {
                            _role = value;
                          });
                        },
                ),
                const SizedBox(height: 16),
                Row(
                  children: [
                    TextButton(
                      onPressed: isSubmitting
                          ? null
                          : () => Navigator.of(context).pop(false),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: AddShopUserSheet.submitButtonKey,
                      onPressed: isSubmitting || _selectedShopIds.isEmpty
                          ? null
                          : () => _submit(state),
                      child: isSubmitting
                          ? SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: theme.colorScheme.onPrimary,
                              ),
                            )
                          : Text(l10n.usersAddUser),
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

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.usersErrorGeneric,
    unauthorized: (String? _) => l10n.usersErrorUnauthorized,
    forbidden: (String? _) => l10n.usersErrorForbidden,
    notFound: (String? _) => l10n.usersErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.usersErrorGeneric,
    network: (String? _) => l10n.usersErrorNetwork,
    timeout: (String? _) => l10n.usersErrorTimeout,
    serialization: (String? _) => l10n.usersErrorGeneric,
    unknown: (String? _) => l10n.usersErrorGeneric,
  );
}
