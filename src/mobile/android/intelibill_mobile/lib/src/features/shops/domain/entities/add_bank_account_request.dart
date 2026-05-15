import 'package:equatable/equatable.dart';

class AddBankAccountRequest extends Equatable {
  const AddBankAccountRequest({
    required this.bankName,
    required this.accountNumber,
    required this.accountType,
    required this.ifscCode,
    required this.accountHolderName,
  });

  final String bankName;
  final String accountNumber;
  final String accountType;
  final String ifscCode;
  final String accountHolderName;

  @override
  List<Object?> get props => [
    bankName,
    accountNumber,
    accountType,
    ifscCode,
    accountHolderName,
  ];
}
