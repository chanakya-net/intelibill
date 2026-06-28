// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_detail_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

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
    r'4d8d626404044b9a8a45c1cb74136b53c692e6d5';

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
