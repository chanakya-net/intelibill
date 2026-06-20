// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sales_history_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(getSalesHistory)
final getSalesHistoryProvider = GetSalesHistoryProvider._();

final class GetSalesHistoryProvider
    extends
        $FunctionalProvider<GetSalesHistory, GetSalesHistory, GetSalesHistory>
    with $Provider<GetSalesHistory> {
  GetSalesHistoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getSalesHistoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getSalesHistoryHash();

  @$internal
  @override
  $ProviderElement<GetSalesHistory> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetSalesHistory create(Ref ref) {
    return getSalesHistory(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetSalesHistory value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetSalesHistory>(value),
    );
  }
}

String _$getSalesHistoryHash() => r'445c0a1295dcfc257b16c46ccab442cde90445d8';

@ProviderFor(SalesHistoryController)
final salesHistoryControllerProvider = SalesHistoryControllerProvider._();

final class SalesHistoryControllerProvider
    extends $NotifierProvider<SalesHistoryController, SalesHistoryState> {
  SalesHistoryControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'salesHistoryControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$salesHistoryControllerHash();

  @$internal
  @override
  SalesHistoryController create() => SalesHistoryController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SalesHistoryState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SalesHistoryState>(value),
    );
  }
}

String _$salesHistoryControllerHash() =>
    r'a37afc1cbdfa9fbdc823fba5adb8974d201d69d9';

abstract class _$SalesHistoryController extends $Notifier<SalesHistoryState> {
  SalesHistoryState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<SalesHistoryState, SalesHistoryState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<SalesHistoryState, SalesHistoryState>,
              SalesHistoryState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
