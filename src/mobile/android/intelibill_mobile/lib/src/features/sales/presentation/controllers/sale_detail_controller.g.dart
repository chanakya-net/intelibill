// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_detail_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(getSaleDetail)
final getSaleDetailProvider = GetSaleDetailProvider._();

final class GetSaleDetailProvider
    extends $FunctionalProvider<GetSaleDetail, GetSaleDetail, GetSaleDetail>
    with $Provider<GetSaleDetail> {
  GetSaleDetailProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getSaleDetailProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getSaleDetailHash();

  @$internal
  @override
  $ProviderElement<GetSaleDetail> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetSaleDetail create(Ref ref) {
    return getSaleDetail(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetSaleDetail value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetSaleDetail>(value),
    );
  }
}

String _$getSaleDetailHash() => r'02d67722dc2fb559107379c47d464d6ecdbb3589';

@ProviderFor(SaleDetailController)
final saleDetailControllerProvider = SaleDetailControllerFamily._();

final class SaleDetailControllerProvider
    extends $NotifierProvider<SaleDetailController, SaleDetailState> {
  SaleDetailControllerProvider._({
    required SaleDetailControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'saleDetailControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$saleDetailControllerHash();

  @override
  String toString() {
    return r'saleDetailControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  SaleDetailController create() => SaleDetailController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SaleDetailState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SaleDetailState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is SaleDetailControllerProvider && other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$saleDetailControllerHash() =>
    r'c39b13d43ce860a68d0bd0514efa23861e3cfaeb';

final class SaleDetailControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          SaleDetailController,
          SaleDetailState,
          SaleDetailState,
          SaleDetailState,
          String
        > {
  SaleDetailControllerFamily._()
    : super(
        retry: null,
        name: r'saleDetailControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  SaleDetailControllerProvider call(String saleId) =>
      SaleDetailControllerProvider._(argument: saleId, from: this);

  @override
  String toString() => r'saleDetailControllerProvider';
}

abstract class _$SaleDetailController extends $Notifier<SaleDetailState> {
  late final _$args = ref.$arg as String;
  String get saleId => _$args;

  SaleDetailState build(String saleId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<SaleDetailState, SaleDetailState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<SaleDetailState, SaleDetailState>,
              SaleDetailState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
