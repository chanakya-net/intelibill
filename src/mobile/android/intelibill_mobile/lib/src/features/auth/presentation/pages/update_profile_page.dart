import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';

const _firstNameFieldKey = Key('update-profile-first-name');
const _lastNameFieldKey = Key('update-profile-last-name');
const _emailFieldKey = Key('update-profile-email');
const _phoneFieldKey = Key('update-profile-phone');
const _submitButtonKey = Key('update-profile-submit');

class UpdateProfilePage extends ConsumerStatefulWidget {
  const UpdateProfilePage({super.key});

  @override
  ConsumerState<UpdateProfilePage> createState() => _UpdateProfilePageState();
}

class _UpdateProfilePageState extends ConsumerState<UpdateProfilePage> {
  final _formKey = GlobalKey<FormState>();
  late TextEditingController _firstNameController;
  late TextEditingController _lastNameController;
  late TextEditingController _emailController;
  late TextEditingController _phoneController;
  bool _wasSubmitting = false;

  @override
  void initState() {
    super.initState();
    _firstNameController = TextEditingController();
    _lastNameController = TextEditingController();
    _emailController = TextEditingController();
    _phoneController = TextEditingController();
  }

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _phoneController.dispose();
    super.dispose();
  }

  void _prefillForm(AuthControllerState state) {
    final user = state.session?.user;
    if (user != null) {
      _firstNameController.text = user.firstName;
      _lastNameController.text = user.lastName;
      _emailController.text = user.email ?? '';
      _phoneController.text = user.phoneNumber ?? '';
    }
  }

  Future<void> _submit(AuthControllerState state) async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    final user = state.session?.user;
    if (user == null) {
      return;
    }

    _wasSubmitting = true;
    await ref
        .read(authControllerProvider.notifier)
        .updateProfile(
          email: _emailController.text.trim(),
          phoneNumber: _phoneController.text.trim().isEmpty
              ? null
              : _phoneController.text.trim(),
          firstName: _firstNameController.text.trim(),
          lastName: _lastNameController.text.trim(),
          language: user.language,
        );
  }

  String? _validateEmail(String? value, AppLocalizations l10n) {
    if ((value ?? '').isEmpty) {
      return l10n.profileEmailRequired;
    }
    const emailRegex = r'^[^@]+@[^@]+\.[^@]+$';
    if (!RegExp(emailRegex).hasMatch(value!)) {
      return l10n.profileEmailInvalid;
    }
    return null;
  }

  String? _validatePhoneNumber(String? value, AppLocalizations l10n) {
    if ((value ?? '').isEmpty) {
      return null; // Phone is optional
    }
    if (value!.length < 10) {
      return l10n.profilePhoneMin;
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    ref.listen(authControllerProvider, (previous, next) {
      next.whenData((state) {
        // Only show success if we were submitting and now have no error
        if (_wasSubmitting && state.errorMessage == null && !state.isLoading) {
          _wasSubmitting = false;
          if (context.mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text(l10n.profileUpdateSuccess)),
            );
            context.pop();
          }
        }
      });
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.profileEditTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: authState.when(
        data: (state) {
          if (state.session == null) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Text(
                  l10n.profileUnableToLoad,
                  style: theme.textTheme.bodyLarge,
                ),
              ),
            );
          }

          // Pre-fill form on first build
          if (_firstNameController.text.isEmpty) {
            _prefillForm(state);
          }

          final isLoading = state.isLoading;

          return SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextFormField(
                    key: _firstNameFieldKey,
                    controller: _firstNameController,
                    enabled: !isLoading,
                    decoration: InputDecoration(
                      labelText: l10n.profileFirstNameLabel,
                      hintText: l10n.profileFirstNameHint,
                    ),
                    validator: (value) {
                      if ((value ?? '').trim().isEmpty) {
                        return l10n.profileFirstNameRequired;
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    key: _lastNameFieldKey,
                    controller: _lastNameController,
                    enabled: !isLoading,
                    decoration: InputDecoration(
                      labelText: l10n.profileLastNameLabel,
                      hintText: l10n.profileLastNameHint,
                    ),
                    validator: (value) {
                      if ((value ?? '').trim().isEmpty) {
                        return l10n.profileLastNameRequired;
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    key: _emailFieldKey,
                    controller: _emailController,
                    enabled: !isLoading,
                    keyboardType: TextInputType.emailAddress,
                    decoration: InputDecoration(
                      labelText: l10n.profileEmailLabel,
                      hintText: l10n.profileEmailHint,
                    ),
                    validator: (value) => _validateEmail(value, l10n),
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    key: _phoneFieldKey,
                    controller: _phoneController,
                    enabled: !isLoading,
                    keyboardType: TextInputType.phone,
                    decoration: InputDecoration(
                      labelText: l10n.profilePhoneLabel,
                      hintText: l10n.profilePhoneHint,
                    ),
                    validator: (value) => _validatePhoneNumber(value, l10n),
                  ),
                  if (state.errorMessage != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(
                          color: Colors.red.shade200,
                        ),
                        color: Colors.red.shade50,
                      ),
                      child: Text(
                        state.errorMessage!,
                        style: TextStyle(
                          color: Colors.red.shade700,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    key: _submitButtonKey,
                    onPressed: isLoading ? null : () => _submit(state),
                    child: isLoading
                        ? Row(
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [
                              const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              ),
                              const SizedBox(width: 12),
                              Text(l10n.profileUpdatingButton),
                            ],
                          )
                        : Text(l10n.profileUpdateButton),
                  ),
                ],
              ),
            ),
          );
        },
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              '${l10n.profileUnableToLoad}. $error',
              textAlign: TextAlign.center,
            ),
          ),
        ),
      ),
    );
  }
}
