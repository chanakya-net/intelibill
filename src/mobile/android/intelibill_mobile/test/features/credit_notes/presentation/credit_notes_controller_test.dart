import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_note_by_code.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_notes.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetCreditNotes extends Mock implements GetCreditNotes {}

class MockGetCreditNoteByCode extends Mock implements GetCreditNoteByCode {}

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

  setUpAll(() {
    registerFallbackValue(const CreditNotesQuery());
  });

  setUp(() {
    getCreditNotes = MockGetCreditNotes();
    getCreditNoteByCode = MockGetCreditNoteByCode();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getCreditNotesUseCaseProvider.overrideWithValue(getCreditNotes),
        getCreditNoteByCodeUseCaseProvider.overrideWithValue(
          getCreditNoteByCode,
        ),
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
          page: 1,
          pageSize: 20,
        ),
      ),
    ).called(1);
  });

  test('verifies code and stores selected note', () async {
    when(() => getCreditNotes(any())).thenAnswer(
      (_) async => CreditNotesResult(
        items: const [],
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
}
