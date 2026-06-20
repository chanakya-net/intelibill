import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/data_sources/credit_note_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/mappers/credit_note_mapper.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';

class CreditNoteRepositoryImpl implements CreditNoteRepository {
  const CreditNoteRepositoryImpl(this._remoteDataSource);

  final CreditNoteRemoteDataSource _remoteDataSource;

  @override
  Future<CreditNotesResult> getCreditNotes(CreditNotesQuery query) async {
    try {
      final dto = await _remoteDataSource.getCreditNotes(
        search: query.search,
        status: query.status,
        page: query.page,
        pageSize: query.pageSize,
      );
      return CreditNoteMapper.listToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<CreditNote> getCreditNoteByCode(String code) async {
    try {
      final dto = await _remoteDataSource.getCreditNoteByCode(code);
      return CreditNoteMapper.toDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<CreditNotePrint> getCreditNotePrintByCode(String code) async {
    try {
      final dto = await _remoteDataSource.getCreditNotePrintByCode(code);
      return CreditNoteMapper.printToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<void> voidCreditNote({
    required String code,
    required String reason,
  }) async {
    try {
      await _remoteDataSource.voidCreditNote(code: code, reason: reason);
    } on AppException {
      rethrow;
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
