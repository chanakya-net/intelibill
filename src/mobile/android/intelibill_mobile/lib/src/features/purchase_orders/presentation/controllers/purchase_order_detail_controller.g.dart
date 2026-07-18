// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_detail_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(PurchaseOrderDetailController)
final purchaseOrderDetailControllerProvider =
    PurchaseOrderDetailControllerFamily._();

final class PurchaseOrderDetailControllerProvider
    extends
        $NotifierProvider<
          PurchaseOrderDetailController,
          PurchaseOrderDetailState
        > {
  PurchaseOrderDetailControllerProvider._({
    required PurchaseOrderDetailControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'purchaseOrderDetailControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderDetailControllerHash();

  @override
  String toString() {
    return r'purchaseOrderDetailControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  PurchaseOrderDetailController create() => PurchaseOrderDetailController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderDetailState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderDetailState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is PurchaseOrderDetailControllerProvider &&
        other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$purchaseOrderDetailControllerHash() =>
    r'59304a3018d115dcfba79aaad65b358e1204e79f';

final class PurchaseOrderDetailControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          PurchaseOrderDetailController,
          PurchaseOrderDetailState,
          PurchaseOrderDetailState,
          PurchaseOrderDetailState,
          String
        > {
  PurchaseOrderDetailControllerFamily._()
    : super(
        retry: null,
        name: r'purchaseOrderDetailControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  PurchaseOrderDetailControllerProvider call(String purchaseOrderId) =>
      PurchaseOrderDetailControllerProvider._(
        argument: purchaseOrderId,
        from: this,
      );

  @override
  String toString() => r'purchaseOrderDetailControllerProvider';
}

abstract class _$PurchaseOrderDetailController
    extends $Notifier<PurchaseOrderDetailState> {
  late final _$args = ref.$arg as String;
  String get purchaseOrderId => _$args;

  PurchaseOrderDetailState build(String purchaseOrderId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<PurchaseOrderDetailState, PurchaseOrderDetailState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<PurchaseOrderDetailState, PurchaseOrderDetailState>,
              PurchaseOrderDetailState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
