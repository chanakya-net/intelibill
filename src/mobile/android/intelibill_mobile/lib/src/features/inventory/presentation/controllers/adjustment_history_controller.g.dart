// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'adjustment_history_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(getAdjustmentHistory)
final getAdjustmentHistoryProvider = GetAdjustmentHistoryProvider._();

final class GetAdjustmentHistoryProvider
    extends
        $FunctionalProvider<
          GetAdjustmentHistory,
          GetAdjustmentHistory,
          GetAdjustmentHistory
        >
    with $Provider<GetAdjustmentHistory> {
  GetAdjustmentHistoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getAdjustmentHistoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getAdjustmentHistoryHash();

  @$internal
  @override
  $ProviderElement<GetAdjustmentHistory> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GetAdjustmentHistory create(Ref ref) {
    return getAdjustmentHistory(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetAdjustmentHistory value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetAdjustmentHistory>(value),
    );
  }
}

String _$getAdjustmentHistoryHash() =>
    r'aba1c7b7afaf612cd01828f83a4d6f8260e2c322';

@ProviderFor(AdjustmentHistoryController)
final adjustmentHistoryControllerProvider =
    AdjustmentHistoryControllerProvider._();

final class AdjustmentHistoryControllerProvider
    extends
        $NotifierProvider<AdjustmentHistoryController, AdjustmentHistoryState> {
  AdjustmentHistoryControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'adjustmentHistoryControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$adjustmentHistoryControllerHash();

  @$internal
  @override
  AdjustmentHistoryController create() => AdjustmentHistoryController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AdjustmentHistoryState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AdjustmentHistoryState>(value),
    );
  }
}

String _$adjustmentHistoryControllerHash() =>
    r'78fc65d3bf07617921dce2542d7fae30f3f3d290';

abstract class _$AdjustmentHistoryController
    extends $Notifier<AdjustmentHistoryState> {
  AdjustmentHistoryState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<AdjustmentHistoryState, AdjustmentHistoryState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AdjustmentHistoryState, AdjustmentHistoryState>,
              AdjustmentHistoryState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
