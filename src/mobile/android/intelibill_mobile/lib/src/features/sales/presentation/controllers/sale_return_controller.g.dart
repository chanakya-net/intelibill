// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_return_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(SaleReturnController)
final saleReturnControllerProvider = SaleReturnControllerFamily._();

final class SaleReturnControllerProvider
    extends $NotifierProvider<SaleReturnController, SaleReturnState> {
  SaleReturnControllerProvider._({
    required SaleReturnControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'saleReturnControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$saleReturnControllerHash();

  @override
  String toString() {
    return r'saleReturnControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  SaleReturnController create() => SaleReturnController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SaleReturnState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SaleReturnState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is SaleReturnControllerProvider && other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$saleReturnControllerHash() =>
    r'55fb1c46b453a70f60625712319931a85799d170';

final class SaleReturnControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          SaleReturnController,
          SaleReturnState,
          SaleReturnState,
          SaleReturnState,
          String
        > {
  SaleReturnControllerFamily._()
    : super(
        retry: null,
        name: r'saleReturnControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  SaleReturnControllerProvider call(String saleId) =>
      SaleReturnControllerProvider._(argument: saleId, from: this);

  @override
  String toString() => r'saleReturnControllerProvider';
}

abstract class _$SaleReturnController extends $Notifier<SaleReturnState> {
  late final _$args = ref.$arg as String;
  String get saleId => _$args;

  SaleReturnState build(String saleId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<SaleReturnState, SaleReturnState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<SaleReturnState, SaleReturnState>,
              SaleReturnState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
