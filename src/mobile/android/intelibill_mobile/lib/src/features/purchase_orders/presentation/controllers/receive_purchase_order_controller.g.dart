// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'receive_purchase_order_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(ReceivePurchaseOrderController)
final receivePurchaseOrderControllerProvider =
    ReceivePurchaseOrderControllerFamily._();

final class ReceivePurchaseOrderControllerProvider
    extends
        $NotifierProvider<
          ReceivePurchaseOrderController,
          ReceivePurchaseOrderState
        > {
  ReceivePurchaseOrderControllerProvider._({
    required ReceivePurchaseOrderControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'receivePurchaseOrderControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$receivePurchaseOrderControllerHash();

  @override
  String toString() {
    return r'receivePurchaseOrderControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  ReceivePurchaseOrderController create() => ReceivePurchaseOrderController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ReceivePurchaseOrderState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ReceivePurchaseOrderState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is ReceivePurchaseOrderControllerProvider &&
        other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$receivePurchaseOrderControllerHash() =>
    r'7f83aec79129e823b0c804ece339a75ef20f5c79';

final class ReceivePurchaseOrderControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          ReceivePurchaseOrderController,
          ReceivePurchaseOrderState,
          ReceivePurchaseOrderState,
          ReceivePurchaseOrderState,
          String
        > {
  ReceivePurchaseOrderControllerFamily._()
    : super(
        retry: null,
        name: r'receivePurchaseOrderControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  ReceivePurchaseOrderControllerProvider call(String purchaseOrderId) =>
      ReceivePurchaseOrderControllerProvider._(
        argument: purchaseOrderId,
        from: this,
      );

  @override
  String toString() => r'receivePurchaseOrderControllerProvider';
}

abstract class _$ReceivePurchaseOrderController
    extends $Notifier<ReceivePurchaseOrderState> {
  late final _$args = ref.$arg as String;
  String get purchaseOrderId => _$args;

  ReceivePurchaseOrderState build(String purchaseOrderId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<ReceivePurchaseOrderState, ReceivePurchaseOrderState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<ReceivePurchaseOrderState, ReceivePurchaseOrderState>,
              ReceivePurchaseOrderState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
