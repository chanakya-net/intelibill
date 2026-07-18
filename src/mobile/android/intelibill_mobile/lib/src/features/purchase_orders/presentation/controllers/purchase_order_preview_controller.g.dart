// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_preview_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(PurchaseOrderPreviewController)
final purchaseOrderPreviewControllerProvider =
    PurchaseOrderPreviewControllerFamily._();

final class PurchaseOrderPreviewControllerProvider
    extends
        $NotifierProvider<
          PurchaseOrderPreviewController,
          PurchaseOrderPreviewState
        > {
  PurchaseOrderPreviewControllerProvider._({
    required PurchaseOrderPreviewControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'purchaseOrderPreviewControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderPreviewControllerHash();

  @override
  String toString() {
    return r'purchaseOrderPreviewControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  PurchaseOrderPreviewController create() => PurchaseOrderPreviewController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderPreviewState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderPreviewState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is PurchaseOrderPreviewControllerProvider &&
        other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$purchaseOrderPreviewControllerHash() =>
    r'ea7ae5e3d62f615d56e74e794570df0ddb3dc381';

final class PurchaseOrderPreviewControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          PurchaseOrderPreviewController,
          PurchaseOrderPreviewState,
          PurchaseOrderPreviewState,
          PurchaseOrderPreviewState,
          String
        > {
  PurchaseOrderPreviewControllerFamily._()
    : super(
        retry: null,
        name: r'purchaseOrderPreviewControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  PurchaseOrderPreviewControllerProvider call(String purchaseOrderId) =>
      PurchaseOrderPreviewControllerProvider._(
        argument: purchaseOrderId,
        from: this,
      );

  @override
  String toString() => r'purchaseOrderPreviewControllerProvider';
}

abstract class _$PurchaseOrderPreviewController
    extends $Notifier<PurchaseOrderPreviewState> {
  late final _$args = ref.$arg as String;
  String get purchaseOrderId => _$args;

  PurchaseOrderPreviewState build(String purchaseOrderId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<PurchaseOrderPreviewState, PurchaseOrderPreviewState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<PurchaseOrderPreviewState, PurchaseOrderPreviewState>,
              PurchaseOrderPreviewState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
