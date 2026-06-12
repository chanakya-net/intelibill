import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';

class EditShopUserSheet extends ConsumerStatefulWidget {
  const EditShopUserSheet({required this.user, super.key});

  final ShopUser user;

  static const submitButtonKey = Key('edit-shop-user-submit');

  @override
  ConsumerState<EditShopUserSheet> createState() => _EditShopUserSheetState();
}

class _EditShopUserSheetState extends ConsumerState<EditShopUserSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _firstNameController;
  late final TextEditingController _lastNameController;
  late final TextEditingController _emailController;
  late final TextEditingController _phoneController;

  late Set<String> _selectedShopIds;
  late String _role;
  late bool _isLoginEnabled;

  @override
  void initState() {
    super.initState();
    final user = widget.user;
    _firstNameController = TextEditingController(text: user.firstName);
    _lastNameController = TextEditingController(text: user.lastName);
    _emailController = TextEditingController(text: user.email ?? '');
    _phoneController = TextEditingController(text: user.phoneNumber ?? '');
    _selectedShopIds = user.shopIds.toSet();
    _role = user.role == 'Manager' ? 'Manager' : 'Staff';
    _isLoginEnabled = user.isLoginEnabled;
  }

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _phoneController.dispose();
    super.dispose();
  }

  List<UserShop> _availableShops(AuthSession? session) {
    return session?.shops ?? const [];
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
        .editShopUser(
          userId: widget.user.userId,
          email: _emailController.text.trim(),
          firstName: _firstNameController.text.trim(),
          lastName: _lastNameController.text.trim(),
          phoneNumber: _phoneController.text.trim(),
          role: _role,
          isLoginEnabled: _isLoginEnabled,
          shopIds: _selectedShopIds.toList(),
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
    final shops = _availableShops(authState.value?.session);
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
                  l10n.usersEditUser,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  l10n.usersEditUserDescription,
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
                  l10n.usersSelectShopsForAccess,
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
                const SizedBox(height: 4),
                SwitchListTile(
                  value: _isLoginEnabled,
                  onChanged: isSubmitting
                      ? null
                      : (value) {
                          setState(() {
                            _isLoginEnabled = value;
                          });
                        },
                  title: Text(l10n.usersAllowLoginLabel),
                  contentPadding: EdgeInsets.zero,
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
                      key: EditShopUserSheet.submitButtonKey,
                      onPressed: isSubmitting || _selectedShopIds.isEmpty
                          ? null
                          : () => _submit(state),
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
