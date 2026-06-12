import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/add_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/edit_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/get_shop_users.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetShopUsers extends Mock implements GetShopUsers {}

class MockAddShopUser extends Mock implements AddShopUser {}

class MockEditShopUser extends Mock implements EditShopUser {}

const _testUsers = [
  ShopUser(
    userId: 'user-1',
    firstName: 'Alice',
    lastName: 'Sharma',
    email: 'alice@example.com',
    phoneNumber: '9876543210',
    role: 'Manager',
    isLoginEnabled: true,
    shopIds: ['shop-1'],
  ),
  ShopUser(
    userId: 'user-2',
    firstName: 'Bob',
    lastName: 'Kumar',
    email: 'bob@example.com',
    phoneNumber: '9123456789',
    role: 'Owner',
    isLoginEnabled: true,
    shopIds: ['shop-1'],
  ),
  ShopUser(
    userId: 'user-3',
    firstName: 'Carol',
    lastName: 'Das',
    email: 'carol@example.com',
    phoneNumber: '9000000001',
    role: 'Staff',
    isLoginEnabled: false,
    shopIds: ['shop-1'],
  ),
];

void main() {
  late MockGetShopUsers getShopUsers;
  late MockAddShopUser addShopUser;
  late MockEditShopUser editShopUser;

  setUp(() {
    getShopUsers = MockGetShopUsers();
    addShopUser = MockAddShopUser();
    editShopUser = MockEditShopUser();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getShopUsersUseCaseProvider.overrideWithValue(getShopUsers),
        addShopUserUseCaseProvider.overrideWithValue(addShopUser),
        editShopUserUseCaseProvider.overrideWithValue(editShopUser),
      ],
    );
  }

  group('UsersController', () {
    test('starts in loading state', () {
      when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(container.read(usersControllerProvider).isLoading, true);
      expect(container.read(usersControllerProvider).users, isEmpty);
    });

    test('loads users and transitions to loaded state', () async {
      when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(usersControllerProvider.notifier).refresh();

      final state = container.read(usersControllerProvider);
      expect(state.isLoading, false);
      expect(state.users, _testUsers);
      expect(state.failure, isNull);
    });

    test('transitions to error state when use case throws', () async {
      when(() => getShopUsers()).thenThrow(Exception('connection failed'));

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(usersControllerProvider.notifier).refresh();

      final state = container.read(usersControllerProvider);
      expect(state.isLoading, false);
      expect(state.users, isEmpty);
      expect(state.failure, isNotNull);
    });

    test('refresh clears error and reloads', () async {
      when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(usersControllerProvider.notifier).refresh();
      await container.read(usersControllerProvider.notifier).refresh();

      verify(() => getShopUsers()).called(greaterThanOrEqualTo(2));
    });

    group('search filtering', () {
      test('filters users by name', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(usersControllerProvider.notifier).refresh();
        container.read(usersControllerProvider.notifier).updateSearch('Alice');

        final filtered = container.read(usersControllerProvider).filteredUsers;
        expect(filtered.length, 1);
        expect(filtered[0].fullName, 'Alice Sharma');
      });

      test('filters users by phone number', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(usersControllerProvider.notifier).refresh();
        container
            .read(usersControllerProvider.notifier)
            .updateSearch('9123456789');

        final filtered = container.read(usersControllerProvider).filteredUsers;
        expect(filtered.length, 1);
        expect(filtered[0].fullName, 'Bob Kumar');
      });

      test('filters users by email', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(usersControllerProvider.notifier).refresh();
        container
            .read(usersControllerProvider.notifier)
            .updateSearch('carol@example.com');

        final filtered = container.read(usersControllerProvider).filteredUsers;
        expect(filtered.length, 1);
        expect(filtered[0].fullName, 'Carol Das');
      });

      test('returns all users when search is cleared', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(usersControllerProvider.notifier).refresh();
        container.read(usersControllerProvider.notifier).updateSearch('Alice');
        container.read(usersControllerProvider.notifier).updateSearch('');

        final filtered = container.read(usersControllerProvider).filteredUsers;
        expect(filtered.length, _testUsers.length);
      });

      test('returns empty list when no match found', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container.read(usersControllerProvider.notifier).refresh();
        container
            .read(usersControllerProvider.notifier)
            .updateSearch('zzznomatch');

        final filtered = container.read(usersControllerProvider).filteredUsers;
        expect(filtered, isEmpty);
      });
    });

    group('add shop user', () {
      test('adds user and refreshes list', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);
        when(
          () => addShopUser(
            shopIds: any(named: 'shopIds'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            password: any(named: 'password'),
            confirmPassword: any(named: 'confirmPassword'),
            role: any(named: 'role'),
          ),
        ).thenAnswer((_) async => _testUsers.first);

        final container = makeContainer();
        addTearDown(container.dispose);
        await container.read(usersControllerProvider.notifier).refresh();

        final result = await container
            .read(usersControllerProvider.notifier)
            .addShopUser(
              shopIds: const ['shop-1'],
              email: 'new@example.com',
              firstName: 'New',
              lastName: 'User',
              phoneNumber: '9000000000',
              password: 'password1',
              confirmPassword: 'password1',
              role: 'Staff',
            );

        expect(result, true);
        final state = container.read(usersControllerProvider);
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isNull);
        verify(() => getShopUsers()).called(greaterThanOrEqualTo(2));
      });

      test('stores submit failure when add throws AppException', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);
        when(
          () => addShopUser(
            shopIds: any(named: 'shopIds'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            password: any(named: 'password'),
            confirmPassword: any(named: 'confirmPassword'),
            role: any(named: 'role'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.validation(message: 'invalid')),
        );

        final container = makeContainer();
        addTearDown(container.dispose);
        await container.read(usersControllerProvider.notifier).refresh();

        final result = await container
            .read(usersControllerProvider.notifier)
            .addShopUser(
              shopIds: const ['shop-1'],
              email: 'new@example.com',
              firstName: 'New',
              lastName: 'User',
              phoneNumber: '9000000000',
              password: 'password1',
              confirmPassword: 'password1',
              role: 'Staff',
            );

        expect(result, false);
        final state = container.read(usersControllerProvider);
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isA<ValidationFailure>());
      });

      test('ignores duplicate add submissions', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        final controller = container.read(usersControllerProvider.notifier)
          ..state = const UsersState(isSubmitting: true);

        final result = await controller.addShopUser(
          shopIds: const ['shop-1'],
          email: 'new@example.com',
          firstName: 'New',
          lastName: 'User',
          phoneNumber: '9000000000',
          password: 'password1',
          confirmPassword: 'password1',
          role: 'Staff',
        );

        expect(result, false);
        verifyNever(
          () => addShopUser(
            shopIds: any(named: 'shopIds'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            password: any(named: 'password'),
            confirmPassword: any(named: 'confirmPassword'),
            role: any(named: 'role'),
          ),
        );
      });
    });

    group('edit shop user', () {
      test('edits user and refreshes list', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);
        when(
          () => editShopUser(
            userId: any(named: 'userId'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            role: any(named: 'role'),
            isLoginEnabled: any(named: 'isLoginEnabled'),
            shopIds: any(named: 'shopIds'),
          ),
        ).thenAnswer((_) async => _testUsers.first);

        final container = makeContainer();
        addTearDown(container.dispose);
        await container.read(usersControllerProvider.notifier).refresh();

        final result = await container
            .read(usersControllerProvider.notifier)
            .editShopUser(
              userId: 'user-1',
              email: 'alice@example.com',
              firstName: 'Alice',
              lastName: 'Updated',
              phoneNumber: '9876543210',
              role: 'Staff',
              isLoginEnabled: false,
              shopIds: const ['shop-1'],
            );

        expect(result, true);
        final state = container.read(usersControllerProvider);
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isNull);
        verify(() => getShopUsers()).called(greaterThanOrEqualTo(2));
      });

      test('stores submit failure when edit throws AppException', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);
        when(
          () => editShopUser(
            userId: any(named: 'userId'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            role: any(named: 'role'),
            isLoginEnabled: any(named: 'isLoginEnabled'),
            shopIds: any(named: 'shopIds'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.forbidden()),
        );

        final container = makeContainer();
        addTearDown(container.dispose);
        await container.read(usersControllerProvider.notifier).refresh();

        final result = await container
            .read(usersControllerProvider.notifier)
            .editShopUser(
              userId: 'user-1',
              email: 'alice@example.com',
              firstName: 'Alice',
              lastName: 'Sharma',
              phoneNumber: '9876543210',
              role: 'Manager',
              isLoginEnabled: true,
              shopIds: const ['shop-1'],
            );

        expect(result, false);
        final state = container.read(usersControllerProvider);
        expect(state.submitFailure, isA<ForbiddenFailure>());
      });

      test('ignores duplicate edit submissions', () async {
        when(() => getShopUsers()).thenAnswer((_) async => _testUsers);

        final container = makeContainer();
        addTearDown(container.dispose);

        final controller = container.read(usersControllerProvider.notifier)
          ..state = const UsersState(isSubmitting: true);

        final result = await controller.editShopUser(
          userId: 'user-1',
          email: 'alice@example.com',
          firstName: 'Alice',
          lastName: 'Sharma',
          phoneNumber: '9876543210',
          role: 'Manager',
          isLoginEnabled: true,
          shopIds: const ['shop-1'],
        );

        expect(result, false);
        verifyNever(
          () => editShopUser(
            userId: any(named: 'userId'),
            email: any(named: 'email'),
            firstName: any(named: 'firstName'),
            lastName: any(named: 'lastName'),
            phoneNumber: any(named: 'phoneNumber'),
            role: any(named: 'role'),
            isLoginEnabled: any(named: 'isLoginEnabled'),
            shopIds: any(named: 'shopIds'),
          ),
        );
      });
    });
  });
}
