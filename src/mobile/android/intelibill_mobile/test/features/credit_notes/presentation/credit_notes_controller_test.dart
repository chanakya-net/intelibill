import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_note_by_code.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_notes.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/void_credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetCreditNotes extends Mock implements GetCreditNotes {}

class MockGetCreditNoteByCode extends Mock implements GetCreditNoteByCode {}

class MockVoidCreditNote extends Mock implements VoidCreditNote {}

CreditNote _note(String code, {String status = 'active'}) {
  return CreditNote(
    creditNoteId: 'cn-$code',
    code: code,
    status: status,
    originalAmount: 250,
    availableBalance: 150,
    expiresAt: null,
    isVoided: false,
    saleReturnId: 'ret-1',
    reason: 'Damaged item',
    voidReason: null,
    returnNumber: 'RET-001',
    invoiceNumber: 'INV-001',
    customerName: 'John',
  );
}

void main() {
  late MockGetCreditNotes getCreditNotes;
  late MockGetCreditNoteByCode getCreditNoteByCode;
  late MockVoidCreditNote voidCreditNote;

  setUpAll(() {
    registerFallbackValue(const CreditNotesQuery());
  });

  setUp(() {
    getCreditNotes = MockGetCreditNotes();
    getCreditNoteByCode = MockGetCreditNoteByCode();
    voidCreditNote = MockVoidCreditNote();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getCreditNotesUseCaseProvider.overrideWithValue(getCreditNotes),
        getCreditNoteByCodeUseCaseProvider.overrideWithValue(
          getCreditNoteByCode,
        ),
        voidCreditNoteUseCaseProvider.overrideWithValue(voidCreditNote),
      ],
    );
  }

  test('loads notes and filters by status', () async {
    when(() => getCreditNotes(any())).thenAnswer(
      (_) async => CreditNotesResult(
        items: [
          _note('CN-001'),
          _note('CN-002', status: 'voided'),
        ],
        totalCount: 2,
        pageNumber: 1,
        pageSize: 20,
      ),
    );

    final container = makeContainer();
    addTearDown(container.dispose);

    await container.read(creditNotesControllerProvider.notifier).refresh();
    await container
        .read(creditNotesControllerProvider.notifier)
        .updateStatusFilter('active');

    expect(container.read(creditNotesControllerProvider).notes.length, 2);
    expect(
      container.read(creditNotesControllerProvider).statusFilter,
      'active',
    );
    verify(
      () => getCreditNotes(
        const CreditNotesQuery(
          search: '',
          status: 'active',
        ),
      ),
    ).called(1);
  });

  test('verifies code and stores selected note', () async {
    when(() => getCreditNotes(any())).thenAnswer(
      (_) async => const CreditNotesResult(
        items: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
      ),
    );
    when(
      () => getCreditNoteByCode('CN-001'),
    ).thenAnswer((_) async => _note('CN-001'));

    final container = makeContainer();
    addTearDown(container.dispose);

    final result = await container
        .read(creditNotesControllerProvider.notifier)
        .verifyCode('CN-001');

    expect(result, isTrue);
    expect(
      container.read(creditNotesControllerProvider).selectedNote?.code,
      'CN-001',
    );
  });

  test('voids active note and refreshes list', () async {
    when(() => getCreditNotes(any())).thenAnswer(
      (_) async => const CreditNotesResult(
        items: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
      ),
    );
    when(
      () => voidCreditNote('CN-001', reason: 'Damaged'),
    ).thenAnswer((_) async {});
    when(() => getCreditNoteByCode('CN-001')).thenAnswer(
      (_) async => _note('CN-001', status: 'voided'),
    );

    final container = makeContainer();
    addTearDown(container.dispose);
    await container.read(creditNotesControllerProvider.notifier).refresh();
    container
        .read(creditNotesControllerProvider.notifier)
        .selectNote(
          _note('CN-001'),
        );

    final result = await container
        .read(creditNotesControllerProvider.notifier)
        .voidActiveNote(code: 'CN-001', reason: 'Damaged');

    expect(result, isTrue);
    verify(() => voidCreditNote('CN-001', reason: 'Damaged')).called(1);
    verify(() => getCreditNotes(any())).called(greaterThanOrEqualTo(2));
    verify(() => getCreditNoteByCode('CN-001')).called(1);
    expect(
      container.read(creditNotesControllerProvider).selectedNote?.code,
      'CN-001',
    );
  });

  test(
    'voidActiveNote sets failure when use case throws AppException',
    () async {
      when(
        () => voidCreditNote('CN-001', reason: 'Damaged'),
      ).thenThrow(
        AppException(failure: const Failure.unknown(message: 'Blocked')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      final result = await container
          .read(creditNotesControllerProvider.notifier)
          .voidActiveNote(code: 'CN-001', reason: 'Damaged');

      expect(result, isFalse);
      expect(
        container.read(creditNotesControllerProvider).failure,
        isA<UnknownFailure>(),
      );
      verify(() => voidCreditNote('CN-001', reason: 'Damaged')).called(1);
    },
  );
}
