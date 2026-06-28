// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discounts_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(discountsRemoteDataSource)
final discountsRemoteDataSourceProvider = DiscountsRemoteDataSourceProvider._();

final class DiscountsRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          DiscountsRemoteDataSource,
          DiscountsRemoteDataSource,
          DiscountsRemoteDataSource
        >
    with $Provider<DiscountsRemoteDataSource> {
  DiscountsRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'discountsRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$discountsRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<DiscountsRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DiscountsRemoteDataSource create(Ref ref) {
    return discountsRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DiscountsRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DiscountsRemoteDataSource>(value),
    );
  }
}

String _$discountsRemoteDataSourceHash() =>
    r'b4e6cd4b84d63ce77608802539d56e077386c2e7';

@ProviderFor(discountsRepository)
final discountsRepositoryProvider = DiscountsRepositoryProvider._();

final class DiscountsRepositoryProvider
    extends
        $FunctionalProvider<
          DiscountsRepository,
          DiscountsRepository,
          DiscountsRepository
        >
    with $Provider<DiscountsRepository> {
  DiscountsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'discountsRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$discountsRepositoryHash();

  @$internal
  @override
  $ProviderElement<DiscountsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DiscountsRepository create(Ref ref) {
    return discountsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DiscountsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DiscountsRepository>(value),
    );
  }
}

String _$discountsRepositoryHash() =>
    r'0c96aaccbe31b4a46edd4d95dd99fed6e91215f0';

@ProviderFor(getDiscountRules)
final getDiscountRulesProvider = GetDiscountRulesProvider._();

final class GetDiscountRulesProvider
    extends
        $FunctionalProvider<
          GetDiscountRules,
          GetDiscountRules,
          GetDiscountRules
        >
    with $Provider<GetDiscountRules> {
  GetDiscountRulesProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getDiscountRulesProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getDiscountRulesHash();

  @$internal
  @override
  $ProviderElement<GetDiscountRules> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetDiscountRules create(Ref ref) {
    return getDiscountRules(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetDiscountRules value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetDiscountRules>(value),
    );
  }
}

String _$getDiscountRulesHash() => r'23dd51c5bc44024faaabeb42e5ec9a063910bc36';

@ProviderFor(getDiscountRuleDetail)
final getDiscountRuleDetailProvider = GetDiscountRuleDetailProvider._();

final class GetDiscountRuleDetailProvider
    extends
        $FunctionalProvider<
          GetDiscountRuleDetail,
          GetDiscountRuleDetail,
          GetDiscountRuleDetail
        >
    with $Provider<GetDiscountRuleDetail> {
  GetDiscountRuleDetailProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getDiscountRuleDetailProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getDiscountRuleDetailHash();

  @$internal
  @override
  $ProviderElement<GetDiscountRuleDetail> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GetDiscountRuleDetail create(Ref ref) {
    return getDiscountRuleDetail(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetDiscountRuleDetail value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetDiscountRuleDetail>(value),
    );
  }
}

String _$getDiscountRuleDetailHash() =>
    r'bc4bdf5db6222c9901b88371ae4fab53365329cd';

@ProviderFor(DiscountsController)
final discountsControllerProvider = DiscountsControllerProvider._();

final class DiscountsControllerProvider
    extends $NotifierProvider<DiscountsController, DiscountsState> {
  DiscountsControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'discountsControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$discountsControllerHash();

  @$internal
  @override
  DiscountsController create() => DiscountsController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DiscountsState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DiscountsState>(value),
    );
  }
}

String _$discountsControllerHash() =>
    r'9226dcb84b7276fffc12bac246dda8db6cb573a9';

abstract class _$DiscountsController extends $Notifier<DiscountsState> {
  DiscountsState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<DiscountsState, DiscountsState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<DiscountsState, DiscountsState>,
              DiscountsState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
