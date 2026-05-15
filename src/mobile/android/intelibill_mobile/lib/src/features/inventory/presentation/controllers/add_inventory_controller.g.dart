// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_inventory_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(addInventoryInbound)
final addInventoryInboundProvider = AddInventoryInboundProvider._();

final class AddInventoryInboundProvider
    extends
        $FunctionalProvider<
          AddInventoryInbound,
          AddInventoryInbound,
          AddInventoryInbound
        >
    with $Provider<AddInventoryInbound> {
  AddInventoryInboundProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'addInventoryInboundProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$addInventoryInboundHash();

  @$internal
  @override
  $ProviderElement<AddInventoryInbound> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  AddInventoryInbound create(Ref ref) {
    return addInventoryInbound(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AddInventoryInbound value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AddInventoryInbound>(value),
    );
  }
}

String _$addInventoryInboundHash() =>
    r'6dff746c1a392ec0da4f823a72e25b9ff28c118a';

@ProviderFor(AddInventoryController)
final addInventoryControllerProvider = AddInventoryControllerProvider._();

final class AddInventoryControllerProvider
    extends $NotifierProvider<AddInventoryController, AddInventoryState> {
  AddInventoryControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'addInventoryControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$addInventoryControllerHash();

  @$internal
  @override
  AddInventoryController create() => AddInventoryController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AddInventoryState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AddInventoryState>(value),
    );
  }
}

String _$addInventoryControllerHash() =>
    r'13cbccc9b466dbe865e790fc62bd011d81041bb7';

abstract class _$AddInventoryController extends $Notifier<AddInventoryState> {
  AddInventoryState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<AddInventoryState, AddInventoryState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AddInventoryState, AddInventoryState>,
              AddInventoryState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
