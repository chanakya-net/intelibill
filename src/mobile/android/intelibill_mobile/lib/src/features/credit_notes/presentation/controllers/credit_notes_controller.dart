import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/data_sources/credit_note_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/repositories/credit_note_repository_impl.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_notes_query.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/repositories/credit_note_repository.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_note_by_code.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_note_print_by_code.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/use_cases/get_credit_notes.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'credit_notes_controller.g.dart';

@riverpod
CreditNoteRemoteDataSource creditNoteRemoteDataSource(Ref ref) {
  return CreditNoteRemoteDataSourceImpl(ref.watch(apiClientProvider));
}

@riverpod
CreditNoteRepository creditNoteRepository(Ref ref) {
  return CreditNoteRepositoryImpl(
    ref.watch(creditNoteRemoteDataSourceProvider),
  );
}

@riverpod
GetCreditNotes getCreditNotesUseCase(Ref ref) {
  return GetCreditNotes(ref.watch(creditNoteRepositoryProvider));
}

@riverpod
GetCreditNoteByCode getCreditNoteByCodeUseCase(Ref ref) {
  return GetCreditNoteByCode(ref.watch(creditNoteRepositoryProvider));
}

@riverpod
GetCreditNotePrintByCode getCreditNotePrintByCodeUseCase(Ref ref) {
  return GetCreditNotePrintByCode(ref.watch(creditNoteRepositoryProvider));
}

@immutable
class CreditNotesState {
  const CreditNotesState({
    this.notes = const [],
    this.searchQuery = '',
    this.statusFilter,
    this.page = 1,
    this.pageSize = 20,
    this.totalCount = 0,
    this.isLoading = false,
    this.isLoadingMore = false,
    this.failure,
    this.selectedNote,
    this.verifiedNote,
  });
  final List<CreditNote> notes;
  final String searchQuery;
  final String? statusFilter;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool isLoading;
  final bool isLoadingMore;
  final Failure? failure;
  final CreditNote? selectedNote;
  final CreditNote? verifiedNote;

  bool get hasMore => page * pageSize < totalCount;

  CreditNotesState copyWith({
    List<CreditNote>? notes,
    String? searchQuery,
    String? statusFilter,
    bool clearStatusFilter = false,
    int? page,
    int? pageSize,
    int? totalCount,
    bool? isLoading,
    bool? isLoadingMore,
    Failure? failure,
    bool clearFailure = false,
    CreditNote? selectedNote,
    bool clearSelectedNote = false,
    CreditNote? verifiedNote,
    bool clearVerifiedNote = false,
  }) {
    return CreditNotesState(
      notes: notes ?? this.notes,
      searchQuery: searchQuery ?? this.searchQuery,
      statusFilter: clearStatusFilter
          ? null
          : (statusFilter ?? this.statusFilter),
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
      totalCount: totalCount ?? this.totalCount,
      isLoading: isLoading ?? this.isLoading,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      failure: clearFailure ? null : (failure ?? this.failure),
      selectedNote: clearSelectedNote
          ? null
          : (selectedNote ?? this.selectedNote),
      verifiedNote: clearVerifiedNote
          ? null
          : (verifiedNote ?? this.verifiedNote),
    );
  }
}

@riverpod
class CreditNotesController extends _$CreditNotesController {
  @override
  CreditNotesState build() {
    unawaited(Future.microtask(refresh));
    return const CreditNotesState(isLoading: true);
  }

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearFailure: true, page: 1);
    await _load(page: 1, append: false);
  }

  Future<void> loadMore() async {
    if (state.isLoading || state.isLoadingMore || !state.hasMore) return;
    state = state.copyWith(isLoadingMore: true, clearFailure: true);
    await _load(page: state.page + 1, append: true);
  }

  Future<void> updateSearch(String query) async {
    state = state.copyWith(searchQuery: query, page: 1);
    await refresh();
  }

  Future<void> updateStatusFilter(String? status) async {
    state = state.copyWith(
      statusFilter: status,
      page: 1,
      clearStatusFilter: status == null,
    );
    await refresh();
  }

  Future<void> clearFilters() async {
    state = state.copyWith(searchQuery: '', clearStatusFilter: true, page: 1);
    await refresh();
  }

  Future<bool> verifyCode(String code) async {
    final normalized = code.trim();
    if (normalized.isEmpty) return false;
    try {
      final note = await ref.read(getCreditNoteByCodeUseCaseProvider)(
        normalized,
      );
      if (!ref.mounted) return false;
      state = state.copyWith(verifiedNote: note, selectedNote: note);
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(failure: error.failure);
      return false;
    } catch (_) {
      if (!ref.mounted) return false;
      state = state.copyWith(failure: const Failure.unknown());
      return false;
    }
  }

  Future<void> openByCode(String code) async {
    await verifyCode(code);
  }

  void selectNote(CreditNote? note) {
    state = state.copyWith(selectedNote: note, clearSelectedNote: note == null);
  }

  Future<void> _load({required int page, required bool append}) async {
    try {
      final result = await ref.read(getCreditNotesUseCaseProvider)(
        CreditNotesQuery(
          search: state.searchQuery,
          status: state.statusFilter,
          page: page,
          pageSize: state.pageSize,
        ),
      );
      if (!ref.mounted) return;
      final notes = append ? [...state.notes, ...result.items] : result.items;
      state = state.copyWith(
        notes: notes,
        page: result.pageNumber,
        pageSize: result.pageSize,
        totalCount: result.totalCount,
        isLoading: false,
        isLoadingMore: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        failure: error.failure,
      );
    } catch (_) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        failure: const Failure.unknown(),
      );
    }
  }
}
