// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_builder_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(PurchaseOrderBuilderController)
final purchaseOrderBuilderControllerProvider =
    PurchaseOrderBuilderControllerFamily._();

final class PurchaseOrderBuilderControllerProvider
    extends
        $NotifierProvider<
          PurchaseOrderBuilderController,
          PurchaseOrderBuilderState
        > {
  PurchaseOrderBuilderControllerProvider._({
    required PurchaseOrderBuilderControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'purchaseOrderBuilderControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderBuilderControllerHash();

  @override
  String toString() {
    return r'purchaseOrderBuilderControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  PurchaseOrderBuilderController create() => PurchaseOrderBuilderController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderBuilderState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderBuilderState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is PurchaseOrderBuilderControllerProvider &&
        other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$purchaseOrderBuilderControllerHash() =>
    r'1c8e803bfb7538908a80e687f13ac98cd8e60c58';

final class PurchaseOrderBuilderControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          PurchaseOrderBuilderController,
          PurchaseOrderBuilderState,
          PurchaseOrderBuilderState,
          PurchaseOrderBuilderState,
          String
        > {
  PurchaseOrderBuilderControllerFamily._()
    : super(
        retry: null,
        name: r'purchaseOrderBuilderControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  PurchaseOrderBuilderControllerProvider call(String target) =>
      PurchaseOrderBuilderControllerProvider._(argument: target, from: this);

  @override
  String toString() => r'purchaseOrderBuilderControllerProvider';
}

abstract class _$PurchaseOrderBuilderController
    extends $Notifier<PurchaseOrderBuilderState> {
  late final _$args = ref.$arg as String;
  String get target => _$args;

  PurchaseOrderBuilderState build(String target);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<PurchaseOrderBuilderState, PurchaseOrderBuilderState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<PurchaseOrderBuilderState, PurchaseOrderBuilderState>,
              PurchaseOrderBuilderState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
