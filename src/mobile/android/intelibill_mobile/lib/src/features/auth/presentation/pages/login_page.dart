import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';

const _loginPageIdentifierFieldKey = Key('login-page-identifier');
const _loginPagePasswordFieldKey = Key('login-page-password');
const _loginPagePasswordVisibilityKey = Key('login-page-password-visibility');
const _loginPageRememberMeKey = Key('login-page-remember-me');
const _loginPageSubmitButtonKey = Key('login-page-submit');
const _loginPageForgotPasswordKey = Key('login-page-forgot-password');
const _loginPageRegisterKey = Key('login-page-register');

class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key});

  @override
  ConsumerState<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends ConsumerState<LoginPage> {
  final _formKey = GlobalKey<FormState>();
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  bool _obscurePassword = true;
  bool _rememberMe = false;
  bool _didLoadRememberedIdentifier = false;

  @override
  void dispose() {
    _identifierController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  void _togglePasswordVisibility() {
    setState(() {
      _obscurePassword = !_obscurePassword;
    });
  }

  Future<void> _submit(AuthControllerState state) async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    await ref
        .read(authControllerProvider.notifier)
        .login(
          identifier: _identifierController.text.trim(),
          password: _passwordController.text,
          rememberMe: _rememberMe,
        );
  }

  Widget _buildGradientBackground(ThemeData theme, Widget child) {
    final gradientEnd =
        theme.navigationBarTheme.indicatorColor ??
        theme.colorScheme.primary.withValues(alpha: 0.15);

    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [theme.scaffoldBackgroundColor, gradientEnd],
        ),
      ),
      child: SafeArea(child: child),
    );
  }

  Widget _buildInlineErrorBanner(ThemeData theme, String message) {
    return Container(
      margin: const EdgeInsets.only(top: 4, bottom: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: theme.colorScheme.error.withValues(alpha: 0.35),
        ),
        color: theme.colorScheme.error.withValues(alpha: 0.08),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.error_outline,
            size: 20,
            color: theme.colorScheme.error,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              message,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.error,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final authState = ref.watch(authControllerProvider);

    // Listen for successful authentication and navigate
    ref.listen(authControllerProvider, (previous, next) {
      next.whenData((state) {
        if (state.isAuthenticated) {
          // User is authenticated, navigate to home
          if (context.mounted) {
            context.go(AppRoutes.dashboard);
          }
        }
      });
    });

    return Scaffold(
      body: authState.when(
        data: (state) {
          if (!_didLoadRememberedIdentifier &&
              !state.isRememberedIdentifierLoading) {
            _identifierController.text = state.rememberedIdentifier;
            _rememberMe = state.rememberMe;
            _didLoadRememberedIdentifier = true;
          }

          return _buildContent(context, theme, state);
        },
        loading: () => _buildGradientBackground(
          theme,
          Center(
            child: CircularProgressIndicator(color: theme.colorScheme.primary),
          ),
        ),
        error: (error, _) => _buildGradientBackground(
          theme,
          Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    Icons.error_outline,
                    size: 48,
                    color: theme.colorScheme.error,
                  ),
                  const SizedBox(height: 12),
                  Text(
                    'Unable to initialize login screen.',
                    style: theme.textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    error.toString(),
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: theme.colorScheme.onSurface.withValues(
                        alpha: 0.7,
                      ),
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                  FilledButton(
                    onPressed: () => ref.invalidate(authControllerProvider),
                    child: const Text('Retry'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildContent(
    BuildContext context,
    ThemeData theme,
    AuthControllerState state,
  ) {
    final isLoading = state.isLoading || state.isRememberedIdentifierLoading;
    final linkButtonStyle = TextButton.styleFrom(
      foregroundColor: theme.colorScheme.primary,
    );

    return _buildGradientBackground(
      theme,
      Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Form(
                  key: _formKey,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'Intelibill',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: theme.colorScheme.primary,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'Login now',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w700,
                          color: theme.colorScheme.onSurface.withValues(
                            alpha: 0.8,
                          ),
                        ),
                      ),
                      const SizedBox(height: 24),
                      TextFormField(
                        key: _loginPageIdentifierFieldKey,
                        controller: _identifierController,
                        enabled: !isLoading,
                        textInputAction: TextInputAction.next,
                        keyboardType: TextInputType.emailAddress,
                        decoration: const InputDecoration(
                          labelText: 'Identifier',
                          hintText: 'Email or phone number',
                          prefixIcon: Icon(Icons.person_outline),
                        ),
                        validator: (value) {
                          if ((value ?? '').trim().isEmpty) {
                            return 'Identifier is required.';
                          }
                          return null;
                        },
                      ),
                      const SizedBox(height: 16),
                      TextFormField(
                        key: _loginPagePasswordFieldKey,
                        controller: _passwordController,
                        enabled: !isLoading,
                        textInputAction: TextInputAction.done,
                        obscureText: _obscurePassword,
                        decoration: InputDecoration(
                          labelText: 'Password',
                          hintText: 'Password',
                          prefixIcon: const Icon(Icons.lock_outline),
                          suffixIcon: IconButton(
                            key: _loginPagePasswordVisibilityKey,
                            onPressed: isLoading
                                ? null
                                : _togglePasswordVisibility,
                            icon: Icon(
                              _obscurePassword
                                  ? Icons.visibility_off_outlined
                                  : Icons.visibility_outlined,
                            ),
                            tooltip: _obscurePassword
                                ? 'Show password'
                                : 'Hide password',
                          ),
                        ),
                        validator: (value) {
                          if ((value ?? '').isEmpty) {
                            return 'Password is required.';
                          }
                          return null;
                        },
                        onFieldSubmitted: (_) => _submit(state),
                      ),
                      const SizedBox(height: 12),
                      Align(
                        alignment: Alignment.centerRight,
                        child: TextButton(
                          key: _loginPageForgotPasswordKey,
                          style: linkButtonStyle,
                          onPressed: isLoading
                              ? null
                              : () {
                                  context.go(AppRoutes.forgotPassword);
                                },
                          child: const Text('Forgot password?'),
                        ),
                      ),
                      if (state.errorMessage != null)
                        _buildInlineErrorBanner(theme, state.errorMessage!),
                      CheckboxListTile(
                        key: _loginPageRememberMeKey,
                        value: _rememberMe,
                        onChanged: isLoading
                            ? null
                            : (value) {
                                setState(() {
                                  _rememberMe = value ?? false;
                                });
                              },
                        title: const Text('Remember me'),
                        contentPadding: EdgeInsets.zero,
                        controlAffinity: ListTileControlAffinity.leading,
                      ),
                      const SizedBox(height: 8),
                      FilledButton(
                        key: _loginPageSubmitButtonKey,
                        onPressed: isLoading ? null : () => _submit(state),
                        child: isLoading
                            ? Row(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  SizedBox(
                                    width: 16,
                                    height: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                      color: theme.colorScheme.onPrimary,
                                    ),
                                  ),
                                  const SizedBox(width: 12),
                                  const Text('Signing in...'),
                                ],
                              )
                            : const Text('Login now'),
                      ),
                      const SizedBox(height: 8),
                      TextButton(
                        key: _loginPageRegisterKey,
                        style: linkButtonStyle,
                        onPressed: isLoading
                            ? null
                            : () {
                                context.go(AppRoutes.register);
                              },
                        child: const Text("Don't have an account? Register"),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
