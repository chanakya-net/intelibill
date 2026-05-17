// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'shop_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(ShopController)
final shopControllerProvider = ShopControllerProvider._();

final class ShopControllerProvider
    extends $AsyncNotifierProvider<ShopController, void> {
  ShopControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'shopControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$shopControllerHash();

  @$internal
  @override
  ShopController create() => ShopController();
}

String _$shopControllerHash() => r'0b5389580321ffc96c505315ac24d7433fcc4324';

abstract class _$ShopController extends $AsyncNotifier<void> {
  FutureOr<void> build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<AsyncValue<void>, void>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AsyncValue<void>, void>,
              AsyncValue<void>,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
