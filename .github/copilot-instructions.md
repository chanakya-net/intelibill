# Project Guidelines

**For the complete and up-to-date project documentation, see [AGENTS.md](../AGENTS.md) at the repo root.**

All guidelines, conventions, build commands, architecture boundaries, and backend/frontend standards are documented in the primary file to maintain consistency across all tools.

## Quick Reference

- Build: `dotnet build src/backend/intelibill/Intelibill.slnx`
- Test: `dotnet test src/backend/intelibill/Intelibill.slnx`
- Frontend: Bun only (not npm/yarn)
- Error handling: ErrorOr result pattern
- Maintainability limits (line, method, file and dependency sizes; error codes; layering): `.agents/rules/maintainability.md`
- Multi-shop: RLS-enforced row isolation per active_shop_id

Architecture docs are per project (`<project>/README.md` and `<project>/architecture_doc/`); the index in [AGENTS.md](../AGENTS.md) says which to read.

See [AGENTS.md](../AGENTS.md) for full details.
