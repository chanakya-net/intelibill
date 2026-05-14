// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'locale_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(localePreferencesStorage)
final localePreferencesStorageProvider = LocalePreferencesStorageProvider._();

final class LocalePreferencesStorageProvider
    extends
        $FunctionalProvider<
          AsyncValue<PreferencesStorage>,
          PreferencesStorage,
          FutureOr<PreferencesStorage>
        >
    with
        $FutureModifier<PreferencesStorage>,
        $FutureProvider<PreferencesStorage> {
  LocalePreferencesStorageProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'localePreferencesStorageProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$localePreferencesStorageHash();

  @$internal
  @override
  $FutureProviderElement<PreferencesStorage> $createElement(
    $ProviderPointer pointer,
  ) => $FutureProviderElement(pointer);

  @override
  FutureOr<PreferencesStorage> create(Ref ref) {
    return localePreferencesStorage(ref);
  }
}

String _$localePreferencesStorageHash() =>
    r'666604c93e55ba60e3a47326f9f06e1690bf2612';

@ProviderFor(LocaleController)
final localeControllerProvider = LocaleControllerProvider._();

final class LocaleControllerProvider
    extends $AsyncNotifierProvider<LocaleController, Locale> {
  LocaleControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'localeControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$localeControllerHash();

  @$internal
  @override
  LocaleController create() => LocaleController();
}

String _$localeControllerHash() => r'087f2d20721a41ece40ed1e308a79d29a72a16d2';

abstract class _$LocaleController extends $AsyncNotifier<Locale> {
  FutureOr<Locale> build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<AsyncValue<Locale>, Locale>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AsyncValue<Locale>, Locale>,
              AsyncValue<Locale>,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
