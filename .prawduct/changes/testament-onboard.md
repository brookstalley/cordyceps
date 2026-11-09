---
schema: 1
type: chore
scope: testament-onboard
date: 2026-11-09
title: run the unit tests on the testament pool
---
**Why:** the owner asked to onboard this repo to their testament pool, so the xUnit suite can run
on the pool's runners rather than only on this machine.

**What changed:** `.testament.toml` (manifest v3, container executor). Its one `files` suite,
`dotnet`, runs the command CI's build-test job runs (`dotnet test
src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`) and reports through the JUnit logger the
test project already references. `.devcontainer/Dockerfile` is the .NET 8 SDK image, pinned by
manifest-list digest, running as the image's non-root `app` user (testament refuses root). In
`docs/testing.md`, the agent block from `testament guide agent-block` is copied
verbatim under a repo note (the block assumes pytest), and `CLAUDE.md` points to it from a short "Running tests" section, which also names the evidence recorder the gates read. The project is allowed on
the pool for container work, and `testament onboard` reports ready: a full pool run passed every test (579 before the develop sync brought in #82; 599 after),
matching a local `dotnet test`. No plugin code changed, and nothing changes for plugin users, so
CHANGELOG.md is untouched. Frictions filed upstream: brookstalley/testament#568–#571.

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
