# Evidence checks shared by the P01-15 runner and its shell regression tests.
require_pass_count() {
  grep -Eq "^  $2 passed \(" "$1" &&
    ! grep -Eq '^  [1-9][0-9]* (failed|skipped|flaky|did not run)' "$1"
}

publish_checked() {
  local candidate="$1" secrets="$2" destination="$3" secret status
  [[ -s "$candidate" && -s "$secrets" ]] || return 1
  while IFS= read -r secret; do
    [[ -n "$secret" ]] || continue
    if grep -Fq -- "$secret" "$candidate"; then return 1; else status=$?; fi
    [[ "$status" == 1 ]] || return 1
  done <"$secrets"
  printf '%s\n' 'PASS fixture credential scan' >>"$candidate"
  mv "$candidate" "$destination"
}
