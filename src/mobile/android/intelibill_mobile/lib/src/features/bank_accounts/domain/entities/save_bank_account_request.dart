import 'package:equatable/equatable.dart';

class SaveBankAccountRequest extends Equatable {
  const SaveBankAccountRequest({
    required this.bankName,
    required this.accountNumber,
    required this.accountType,
    this.ifscCode,
    this.accountHolderName,
  });

  final String bankName;
  final String accountNumber;
  final String accountType;
  final String? ifscCode;
  final String? accountHolderName;

  @override
  List<Object?> get props => [
    bankName,
    accountNumber,
    accountType,
    ifscCode,
    accountHolderName,
  ];
}
