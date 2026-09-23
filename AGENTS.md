# Repository Guidelines

## Behavioral Guidelines

1. **Think Before Coding** — State assumptions; if uncertain, ask. Present multiple interpretations instead of picking silently. Propose simpler approaches and push back when warranted.
2. **Simplicity First** — Minimum code that solves the problem. Nothing speculative: no abstractions for single-use code, no config for unrequested features, no error handling for impossible scenarios. If 200 lines could be 50, rewrite it.
3. **Surgical Changes** — Touch only what your task requires; match existing style. Don't "improve" adjacent code. Remove orphans created by YOUR changes; mention (don't delete) pre-existing dead code.
4. **Goal-Driven Execution** — Define verifiable success criteria before starting; loop until verified. State a brief plan for multi-step tasks: `1. [Step] → verify: [check]`.

## Knowledge Persistence

1. **Persist knowledge at the source** — When a conversation produces reusable rules, compatibility notes, troubleshooting conclusions, or project knowledge, write them into the corresponding module `AGENTS.md` (the one closest to the code), and keep the root `AGENTS.md` module index in sync (module content is authoritative).
2. **Fix stale index entries proactively** — If the module index in root `AGENTS.md` is outdated or inaccurate, update it without being asked.
3. **Keep docs attached to code changes** — When adding or modifying a feature, update the relevant module `AGENTS.md` and link the corresponding development/refactor documents.
4. **Triage before writing (AGENTS.md = rules, not history)** — Before adding anything to a module `AGENTS.md`, apply the three questions:
   - *Rule or incident?* Rules that prevent regression or change how code in this module is written today → AGENTS.md as an imperative ("must/never"). Fixed incidents with only story value → `doc/bugfix/` only.
   - *Does the code say it?* If an assert, test, constant, or comment already enforces the lesson → do NOT write it in AGENTS.md (code is the source of truth); delete any stale entry it supersedes.
   - *Same topic already covered?* Merge into one general rule instead of adding a new line.
   - Maintenance: when adding an entry, sweep the file — remove entries now enforced by code, merge duplicates. Keep module guides ≤ ~40 lines and "Pitfalls" sections ≤ 5 entries; overflow moves to `doc/bugfix/` leaving at most a one-line link.
5. **Bug post-mortems go to `doc/bugfix/`** — one file per incident, linked from the index (`doc/bugfix/README.md`). AGENTS.md entries may reference it, but the detailed narrative lives there, never in a module guide.
6. **New issues unrelated to the current task go to `doc/issue/`** — when a bug is discovered while working a dev task but is not part of that task, record it as one file per issue under `doc/issue/` (with an index row in `doc/issue/README.md`) and continue the task. When the issue is later resolved, delete its file and index row. Never let task-scope issues block the task; never let them be fixed ad hoc inside an unrelated change.
7. **Docs must be plain and easy to understand** — write for a colleague six months from now, not for yourself today: first say in one sentence what problem this solves, then explain how it works. Explain a term when it first appears; prefer concrete numbers and examples over vague wording. Self-check before writing: could someone who has never seen this code reconstruct what happened from this text alone?
8. **Every doc carries a status header** — new files under `doc/` start with `> 状态：规划中 · YYYY-MM` (or 参考资料) right after the H1; vocabulary and the directory map live in `doc/README.md`. Conversation transcripts / model-named dumps must never be committed — distill findings into module AGENTS.md, a bugfix/issue entry, or an existing plan doc, then discard the raw text (git history keeps deleted docs recoverable).
9. **Doc lifecycle** — when a plan finishes: distill durable rules into the module `AGENTS.md`, flip its status to `已实施 · date`, add a row to that directory's README index. Executed plans with no external inbound links move to `doc/archive/<topic>/`; load-bearing docs referenced from AGENTS.md/code stay in place. Superseded docs get `已废弃 → successor-link` in their status header instead of deletion when history matters.
10. **Lint doc/ after each milestone** — sweep for: orphan files (no inbound links), status headers contradicting code reality, stale claims superseded by newer docs, duplicate/similarly-named docs, missing cross-references. Apply fixes in the same pass; archive or link what has drifted.

## Development Conventions

- **Commits:** Conventional Commits — `<type>(<scope>): <subject>` (feat/fix/docs/style/refactor/perf/test/build/ci/chore/revert). Subject = one-line summary; body = bullet points, one per change (`- ...`); link related post-mortems as a bullet (`- post-mortem: doc/bugfix/<file>.md`).