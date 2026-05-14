import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/auth/data/repositories/auth_repository_impl.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:shared_preferences/shared_preferences.dart';

part 'auth_controller.g.dart';

const _invalidCredentialsTitle = 'Auth.InvalidCredentials';
const _disabledAccountTitle = 'Auth.UserLoginDisabled';

const _invalidCredentialsMessage = 'Invalid identifier or password.';
const _disabledAccountMessage = 'This account has been disabled.';
const _networkErrorMessage = 'Unable to connect. Please check your network.';
const _timeoutErrorMessage = 'Login timed out. Please try again.';
const _genericSignInErrorMessage = 'Unable to sign in. Please try again.';

@riverpod
SecureStorage secureStorage(Ref ref) {
  return SecureStorageImpl();
}

@riverpod
Future<PreferencesStorage> preferencesStorage(Ref ref) async {
  final prefs = await SharedPreferences.getInstance();
  return PreferencesStorageImpl(prefs);
}

@riverpod
AuthRemoteDataSource authRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return AuthRemoteDataSourceImpl(apiClient);
}

@riverpod
Future<AuthRepository> authRepository(Ref ref) async {
  final remoteDataSource = ref.watch(authRemoteDataSourceProvider);
  final secureStorage = ref.watch(secureStorageProvider);
  final preferencesStorage = await ref.watch(preferencesStorageProvider.future);

  return AuthRepositoryImpl(
    remoteDataSource: remoteDataSource,
    secureStorage: secureStorage,
    preferencesStorage: preferencesStorage,
  );
}

@immutable
class AuthControllerState {
  const AuthControllerState({
    this.session,
    this.errorMessage,
    this.isLoading = false,
    this.isRememberedIdentifierLoading = false,
    this.rememberedIdentifier = '',
    this.rememberMe = false,
  });

  final AuthSession? session;
  final String? errorMessage;
  final bool isLoading;
  final bool isRememberedIdentifierLoading;
  final String rememberedIdentifier;
  final bool rememberMe;

  bool get isAuthenticated => session != null;

  AuthControllerState copyWith({
    AuthSession? session,
    String? errorMessage,
    bool? isLoading,
    bool? isRememberedIdentifierLoading,
    String? rememberedIdentifier,
    bool? rememberMe,
    bool clearError = false,
    bool clearSession = false,
  }) {
    return AuthControllerState(
      session: clearSession ? null : (session ?? this.session),
      errorMessage: clearError ? null : (errorMessage ?? this.errorMessage),
      isLoading: isLoading ?? this.isLoading,
      isRememberedIdentifierLoading:
          isRememberedIdentifierLoading ?? this.isRememberedIdentifierLoading,
      rememberedIdentifier: rememberedIdentifier ?? this.rememberedIdentifier,
      rememberMe: rememberMe ?? this.rememberMe,
    );
  }

  @override
  String toString() {
    return 'AuthControllerState('
        'isLoading: $isLoading, '
        'isRememberedIdentifierLoading: $isRememberedIdentifierLoading, '
        'rememberMe: $rememberMe, '
        'hasSession: ${session != null}, '
        'errorMessage: $errorMessage'
        ')';
  }
}

@riverpod
class AuthController extends _$AuthController {
  @override
  Future<AuthControllerState> build() async {
    final repository = await ref.watch(authRepositoryProvider.future);
    const state = AuthControllerState(isRememberedIdentifierLoading: true);

    try {
      final rememberedIdentifier = await repository.getRememberedIdentifier();
      return state.copyWith(
        isRememberedIdentifierLoading: false,
        rememberedIdentifier: rememberedIdentifier ?? '',
        rememberMe: (rememberedIdentifier ?? '').isNotEmpty,
      );
    } on AppException catch (error) {
      return state.copyWith(
        isRememberedIdentifierLoading: false,
        errorMessage: _mapAuthFailureToMessage(error.failure),
      );
    }
  }

  Future<void> login({
    required String identifier,
    required String password,
    required bool rememberMe,
  }) async {
    final repository = await ref.read(authRepositoryProvider.future);
    if (!ref.mounted) {
      return;
    }

    final current = state.value ?? const AuthControllerState();
    final trimmedIdentifier = identifier.trim();

    state = AsyncData(
      current.copyWith(
        isLoading: true,
        clearError: true,
        rememberMe: rememberMe,
        rememberedIdentifier: trimmedIdentifier,
      ),
    );

    try {
      final session = await repository.login(
        identifier: trimmedIdentifier,
        password: password,
        rememberMe: rememberMe,
      );
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        AuthControllerState(
          session: session,
          rememberMe: rememberMe,
          rememberedIdentifier: trimmedIdentifier,
        ),
      );
    } on AppException catch (error) {
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        (state.value ?? const AuthControllerState()).copyWith(
          isLoading: false,
          rememberMe: rememberMe,
          errorMessage: _mapAuthFailureToMessage(error.failure),
        ),
      );
    } on Object {
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        (state.value ?? const AuthControllerState()).copyWith(
          isLoading: false,
          rememberMe: rememberMe,
          errorMessage: _genericSignInErrorMessage,
        ),
      );
    }
  }

  Future<void> signOut() async {
    final repository = await ref.read(authRepositoryProvider.future);
    if (!ref.mounted) {
      return;
    }

    final current = state.value ?? const AuthControllerState();
    state = AsyncData(current.copyWith(isLoading: true, clearError: true));

    try {
      await repository.clearTokens();
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        current.copyWith(
          isLoading: false,
          clearSession: true,
          clearError: true,
        ),
      );
    } on AppException catch (error) {
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        current.copyWith(
          isLoading: false,
          errorMessage: _mapAuthFailureToMessage(error.failure),
        ),
      );
    } on Object {
      if (!ref.mounted) {
        return;
      }
      state = AsyncData(
        current.copyWith(
          isLoading: false,
          errorMessage: _genericSignInErrorMessage,
        ),
      );
    }
  }

  void clearError() {
    final current = state.value;
    if (current == null) {
      return;
    }
    state = AsyncData(current.copyWith(clearError: true));
  }

  void clearState() {
    final current = state.value;
    if (current == null) {
      return;
    }
    state = AsyncData(
      current.copyWith(
        clearSession: true,
        clearError: true,
        isLoading: false,
        isRememberedIdentifierLoading: false,
      ),
    );
  }
}

String _mapAuthFailureToMessage(Failure failure) {
  return failure.when(
    validation: (message, errors) {
      final firstValidationMessage = _extractValidationMessage(errors, message);
      return firstValidationMessage ?? _genericSignInErrorMessage;
    },
    unauthorized: (message) {
      if (message == _invalidCredentialsTitle) {
        return _invalidCredentialsMessage;
      }
      return _genericSignInErrorMessage;
    },
    forbidden: (message) {
      if (message == _disabledAccountTitle) {
        return _disabledAccountMessage;
      }
      return _genericSignInErrorMessage;
    },
    notFound: (_) => _genericSignInErrorMessage,
    server: (message, _) => message ?? _genericSignInErrorMessage,
    network: (_) => _networkErrorMessage,
    timeout: (_) => _timeoutErrorMessage,
    serialization: (_) => _genericSignInErrorMessage,
    unknown: (_) => _genericSignInErrorMessage,
  );
}

String? _extractValidationMessage(
  Map<String, List<String>>? errors,
  String? fallbackMessage,
) {
  final fieldErrors = errors;
  if (fieldErrors == null || fieldErrors.isEmpty) {
    return fallbackMessage;
  }

  final firstEntry = fieldErrors.values.firstWhere(
    (value) => value.isNotEmpty,
    orElse: () => const [],
  );
  if (firstEntry.isEmpty) {
    return fallbackMessage;
  }

  return firstEntry.first;
}
